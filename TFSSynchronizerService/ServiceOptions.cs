using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BuildDataDriver.Data;
using BuildDataDriver.Interfaces;
using BuildDataDriver.tools;
using FTIPusher.Util;
using NLog;

namespace FTIPusher
{
    
    public class ServiceCoreLogic
    {
        private readonly TfsOps _tfsOps;
        private Logger _logger;
        private readonly string _dataBaseLogDirPath;
        private bool _stopUpdates = false;
        private Object _thisLock = new Object();
        private readonly HTTPHelper _httpHelper;
        private readonly string _databaseSubDir = "FTIDeployer";

        public bool StopUpdates
        {
            get { return _stopUpdates; }
            set
            {
                _logger.Info("Stop has been requested in SerivceCoreLogic");
                _stopUpdates = value;
            }
        }

        public bool HasStopped { get; set; }

        /// <summary>
        /// Hold an instance of NLogger
        /// </summary>
        public Logger Logger
        {
            set { _logger = value; }
            get { return _logger; }
        }

        public ServiceCoreLogic(Logger logger)
        {
            _logger = logger;
            StopUpdates = false;
            HasStopped = false;
            _dataBaseLogDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), _databaseSubDir);
            _tfsOps = new TfsOps(_dataBaseLogDirPath);
            _logger.Info("DATABASE PATH SET: {0}", _dataBaseLogDirPath);
            _httpHelper = new HTTPHelper(this);
        }

        public bool RunPusherLogic(ServiceOptionsRoot serviceOptions)
        {
            if (StopUpdates == true)
            {
                HasStopped = true;
                return false;
            }

            // Captures all the data needed from TFS about the various packages
            Dictionary<IDynamicSourceDetails, BuildPackageInformation> buildQueryManifest =  new Dictionary<IDynamicSourceDetails, BuildPackageInformation>();
            
            // Use a GUID for log parsing - since this application run in parallel, the log entries are not in order
            var guid = Guid.NewGuid();
            _logger.Info("$$$ {0} $$$ Core logic START", guid);

            // Compute all build queries up front....
            if ((serviceOptions.RunBuildUpdate) || (serviceOptions.RunFtpUpload))
            {
                // Used the options file to query for the list of branches/projects to operate on
                var branchList = _tfsOps.GetBranchList(serviceOptions.BuildsToWatch.ToList<IBuildsToWatch>());
                
                // Validate that the buidls exist and put together the data needed to acquire them
                var validateAndRefineBuildsToWatch = _tfsOps.ValidateAndRefineBuildsToWatch(branchList, serviceOptions.BuildsToWatch.ToList<IBuildsToWatch>());

                // Continue to build up some additional package infrmation and store what is needed in buildQueryManifest 
                foreach (var dynamicSourceDetails in validateAndRefineBuildsToWatch)
                {
                    BuildPackageInformation buildInformation = null;
                    try
                    {
                        buildInformation = _tfsOps.Query(dynamicSourceDetails.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Exception querrying build system!", ex);
                    }

                    if ((buildInformation != null) && (buildInformation.WatchBuild))
                    {
                        _logger.Info("Adding build to watch to Queue: {0} - {1}, VERSION: {2}", dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch, buildInformation.GetDeploymentVersionNumber());
                        lock (_thisLock)
                        {
                            buildQueryManifest.Add(dynamicSourceDetails.Key, buildInformation);
                        }
                    }
                    else
                    {
                        _logger.Error("Skipping addition of build to the queue because Query failed: {0} - {1}", dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch);
                    }

                }
            }


            var calculatedCount = buildQueryManifest.Count;
            var expectedCount = serviceOptions.BuildsToWatch.Count;
            _logger.Info("Found {0} builds to watch, Main operations starting....", calculatedCount);
            if (calculatedCount != expectedCount)
                _logger.Error("Expected {0} builds to watch but only found {1}", expectedCount, calculatedCount);


            List<KeyValuePair<string,IDynamicSourceDetails>> buildUpdateManifest = new List<KeyValuePair<string, IDynamicSourceDetails>>();

            // CORE LOGIC LOOP
            Parallel.ForEach(buildQueryManifest, buildsToWatch =>
            {

                _logger.Info("Watching Build: {0} - {1}", buildsToWatch.Key.Project, buildsToWatch.Key.Branch);

                if (serviceOptions.RunBuildUpdate)
                {
                    try
                    {
                        var buildInformation = buildsToWatch.Value;
                        if (buildInformation != null)
                        {
                            _logger.Info("Acquired build information from TFS server");
                            var strPath = FileUtils.GetFullFormatedPathForBuild(serviceOptions.StagingFileShare,
                                buildsToWatch.Key, buildInformation);
                            var count = FileUtils.XCopyFilesParallel(strPath, buildInformation.InstallationComponents,_logger);
                            var expectedCnt = buildInformation.InstallationComponents.Count;
                            if (count != expectedCnt)
                            {
                                _logger.Error(
                                    "ERROR: XCopy might have failed to copy all required components!  Expected: {0} - Copied {1}",
                                    expectedCnt, count);
                            }

                            // Add this item to the manifest for easy process later down the line.
                            buildUpdateManifest.Add(new KeyValuePair<string, IDynamicSourceDetails>(strPath, buildsToWatch.Key));

                            var verNumber = buildInformation.GetDeploymentVersionNumber();
                            if (!string.IsNullOrEmpty(buildInformation.GetDeploymentVersionNumber()))
                                _tfsOps.SetDbDeployedStatus(buildsToWatch.Key.Project, buildsToWatch.Key.Branch,buildsToWatch.Key.SubBranch, verNumber, true);

                            try
                            {
                                foreach (var file in Directory.GetFiles(strPath, "*.exe", SearchOption.TopDirectoryOnly))
                                {
                                    _tfsOps.UpdateCheckSum(buildsToWatch.Key.Project, buildsToWatch.Key.Branch, buildsToWatch.Key.SubBranch, verNumber, file);
                                }
                            }
                            catch (Exception)
                            {
                                // ignored
                            }
                        }
                        else
                        {
                            _logger.Info("No build information found from TFS server");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.InfoException("Exception Querying Build", ex);
                    }
                }

                ///////////////////////////////////////////////////////////
                // Clean up
                ///////////////////////////////////////////////////////////
                try
                {
                    CleanUpCacheShare(serviceOptions, _logger, buildsToWatch.Key);
                }
                catch (Exception ex)
                {
                    _logger.InfoException("Exception Cleaning Cache Server", ex);
                }
            });

            if (serviceOptions.RunFtpUpload)
            {
                _logger.Info("RunFtpUpload started");

                //THROTTLE the upload to 4 in parallel
                LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(10);
                TaskFactory factory = new TaskFactory(lcts);
                List<Task> tasks = new List<Task>();
                
                foreach (var buildUpdateItem in buildUpdateManifest)
                {
                    var currentTarget = buildUpdateItem;
                    foreach (var ftpLocation in serviceOptions.FTPLocations)
                    {
                        var ftpTarget = ftpLocation;
                        FtpOperations ftpOperations = new FtpOperations(ftpTarget.FtpUser, ftpTarget.FtpPassWord, ftpTarget.FtpUrl, ftpTarget.FtpProxy, ftpTarget.FtpPort, _logger);
                        Task t = factory.StartNew(() => ftpOperations.MirrorDirectory(currentTarget.Key, ftpTarget.FTPDirectory, serviceOptions.StagingFileShare, ftpTarget.FtpUrl),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning, lcts);
                            tasks.Add(t);
                    }
                }
                Task.WaitAll(tasks.ToArray());
                _logger.Info("Completed Processing of FTP");
                _logger.Info("RunFtpUpload completed");
            }

            if (serviceOptions.RunMirrorCopy)
            {

                Parallel.ForEach(serviceOptions.Mirror, mirrorLocation =>
                {
                    List<Task> tasks = new List<Task>();

                        LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(10);
                        TaskFactory factory = new TaskFactory(lcts);
                        _logger.Info("Processing Remote Mirror : {0}", mirrorLocation);

                        DirectoryInfo di = new DirectoryInfo(serviceOptions.StagingFileShare);
                        foreach (var directoryInfo in di.GetDirectories("*", SearchOption.TopDirectoryOnly))
                        {
                            var destLocation = FileUtils.GetFormatedPathForBuild(mirrorLocation, directoryInfo.Name);
                            var soureLocation = directoryInfo.FullName;
                            Task t =
                                factory.StartNew(
                                    () =>
                                        FileUtils.MirrorDirectory(soureLocation, destLocation, _logger,
                                            serviceOptions.MirrorRetryNumber, _dataBaseLogDirPath),
                                    CancellationToken.None,
                                    TaskCreationOptions.LongRunning, lcts);
                            tasks.Add(t);
                        }
                    
                    _logger.Info("Location: {0} - Remote number of copies to perform for this branch: {1}", mirrorLocation, tasks.Count);
                    Task.WaitAll(tasks.ToArray());
                    _logger.Info("Completed Processing of Remote Mirror : {0}", mirrorLocation);
                });
            }

            if (serviceOptions.RunHttpMirror)
            {
                _logger.Info("HTTP Mirror process starting....");

                DirectoryInfo[] directoryTopLevel = new DirectoryInfo[] { };

                try
                {
                    // Acquire local base directory to use to sync out to the remote shares
                    var di = new DirectoryInfo(serviceOptions.StagingFileShare);
                    directoryTopLevel = di.GetDirectories("*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception)
                {
                    _logger.ErrorException(
                        "Exception for local folders to sync via HTTP: " + serviceOptions.StagingFileShare,
                        exception);
                }

                _httpHelper.RunCoreHTTPSync(serviceOptions, directoryTopLevel);
                _logger.Info("HTTP Mirror process complete....");
            }

        
            /////////////////////////////////////////////////////////////
            //// Clean up Logs
            /////////////////////////////////////////////////////////////
            CleanUpLogDir(_dataBaseLogDirPath, serviceOptions.MirrorLogRetention, _logger);

            _logger.Info("$$$ {0} $$$ Core logic END", guid);

            return true;
        }

        //private void CleanUpCacheShare(ServiceOptionsRoot serviceOptions, Logger logger, BuildsToWatch buildsToWatch)
        private void CleanUpCacheShare(ServiceOptionsRoot serviceOptions, Logger logger, IDynamicSourceDetails buildsToWatch)
        {
            logger.Info("Cleaning local cache");
            // RUN LOCAL CLEAN UP PROCESS
            var cleanUpDirectories =
                FileUtils.CleanUpDirectories(FileUtils.GetFormatedPathForBuild(serviceOptions.StagingFileShare,
                    buildsToWatch.Branch, buildsToWatch.SubBranch), buildsToWatch.Retention, logger);

            foreach (var cleanUpDirectory in cleanUpDirectories)
            {
                logger.Info("Removed Directory: {0}", cleanUpDirectory);
                // Need to do something better with Project name and maybe mark status as removed or clean up the record.
                _tfsOps.SetDbDeployedStatus(buildsToWatch.Project, buildsToWatch.Branch,buildsToWatch.SubBranch, cleanUpDirectory.Value, false);
            }
            logger.Info("Cleaning local cache complete");
        }

        private void CleanUpLogDir(string logDirPath, int retention, Logger logger)
        {
            logger.Info("Cleaning log directory");
            // RUN LOCAL CLEAN UP PROCESS
            FileUtils.CleanUpFiles(logDirPath, retention, logger);

            logger.Info("Cleaning log directory complete");
        }
    }

}
