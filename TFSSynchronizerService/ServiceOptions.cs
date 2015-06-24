using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public ServiceCoreLogic(Logger logger, ServiceOptionsRoot serviceOptions)
        {
            _logger = logger;
            StopUpdates = false;
            HasStopped = false;
            _dataBaseLogDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                _databaseSubDir);
            _tfsOps = new TfsOps(_dataBaseLogDirPath, serviceOptions.BuildServer);
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
            Dictionary<IDynamicSourceDetails, BuildPackageInformation> buildQueryManifest =
                new Dictionary<IDynamicSourceDetails, BuildPackageInformation>();

            // Use a GUID for log parsing - since this application run in parallel, the log entries are not in order
            var guid = Guid.NewGuid();
            _logger.Info("$$$ {0} $$$ Core logic START", guid);
            _logger.Info("********************************************************************");
            _logger.Info("OPTIONS SET: ", guid);
            _logger.Info("RunBuildUpdate: {0}", serviceOptions.RunBuildUpdate);
            _logger.Info("RunFtpUpload: {0}", serviceOptions.RunFtpUpload);
            _logger.Info("RunMirrorCopy: {0}", serviceOptions.RunMirrorCopy);
            _logger.Info("RunHttpMirror: {0}", serviceOptions.RunHttpMirror);
            _logger.Info("RunCheckSum: {0}", serviceOptions.RunCheckSum);
            _logger.Info("********************************************************************");

            // Compute all build queries up front....
            if ((serviceOptions.RunBuildUpdate) || (serviceOptions.RunFtpUpload))
            {
                ComputeUpdateData(serviceOptions, ref buildQueryManifest);
            }

            var calculatedCount = buildQueryManifest.Count;
            var expectedCount = serviceOptions.BuildsToWatch.Count;
            _logger.Info("Found {0} builds to watch, Main operations starting....", calculatedCount);
            if (calculatedCount != expectedCount)
                _logger.Error("Expected {0} builds to watch but only found {1}", expectedCount, calculatedCount);

            List<KeyValuePair<string, IDynamicSourceDetails>> buildUpdateManifest =
                new List<KeyValuePair<string, IDynamicSourceDetails>>();


            /////////////////////////////////////////////////////////////
            //// Core logic
            /////////////////////////////////////////////////////////////

            if (serviceOptions.RunBuildUpdate)
            {
                RunBuildUpdate(serviceOptions, buildQueryManifest, buildUpdateManifest);

                RunBuildCleanUp(serviceOptions, buildQueryManifest);
            }

            if (serviceOptions.RunFtpUpload)
            {
                RunFTPUpload(serviceOptions, buildUpdateManifest);
            }

            if (serviceOptions.RunMirrorCopy)
            {

                RunMirrorCopy(serviceOptions);
            }

            if (serviceOptions.RunHttpMirror)
            {
                RunHttpMirror(serviceOptions);
            }


            /////////////////////////////////////////////////////////////
            //// Clean up Logs
            /////////////////////////////////////////////////////////////
            CleanUpLogDir(_dataBaseLogDirPath, serviceOptions.MirrorLogRetention, _logger);

            _logger.Info("$$$ {0} $$$ Core logic END", guid);

            return true;
        }

        /// <summary>
        /// Updates Rest based HTTP Server packages
        /// </summary>
        /// <param name="serviceOptions"></param>
        private void RunHttpMirror(ServiceOptionsRoot serviceOptions)
        {
            _logger.Info("HTTP Mirror process starting....");

            DirectoryInfo[] directoryTopLevel = new DirectoryInfo[] {};

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

        /// <summary>
        /// Mirrors local package repository to a remote location
        /// </summary>
        /// <param name="serviceOptions"></param>
        private void RunMirrorCopy(ServiceOptionsRoot serviceOptions)
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

                _logger.Info("Location: {0} - Remote number of copies to perform for this branch: {1}",
                    mirrorLocation, tasks.Count);
                Task.WaitAll(tasks.ToArray());
                _logger.Info("Completed Processing of Remote Mirror : {0}", mirrorLocation);
            });
        }

        /// <summary>
        /// Mirrors local package repository to an FTP location
        /// </summary>
        /// <param name="serviceOptions"></param>
        /// <param name="buildUpdateManifest"></param>
        private void RunFTPUpload(ServiceOptionsRoot serviceOptions, List<KeyValuePair<string, IDynamicSourceDetails>> buildUpdateManifest)
        {
            _logger.Info("**== RunFtpUpload started ==**");

            //THROTTLE the upload
            LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(10);
            TaskFactory factory = new TaskFactory(lcts);
            List<Task> tasks = new List<Task>();

            foreach (var buildUpdateItem in buildUpdateManifest)
            {
                var currentTarget = buildUpdateItem;
                foreach (var ftpLocation in serviceOptions.FTPLocations)
                {
                    var ftpTarget = ftpLocation;
                    FtpOperations ftpOperations = new FtpOperations(ftpTarget.FtpUser, ftpTarget.FtpPassWord,
                        ftpTarget.FtpUrl, ftpTarget.FtpProxy, ftpTarget.FtpPort, _logger);
                    Task t =
                        factory.StartNew(
                            () =>
                                ftpOperations.MirrorDirectory(currentTarget.Key, ftpTarget.FTPDirectory,
                                    serviceOptions.StagingFileShare, ftpTarget.FtpUrl),
                            CancellationToken.None,
                            TaskCreationOptions.LongRunning, lcts);
                    tasks.Add(t);
                }
            }
            Task.WaitAll(tasks.ToArray());
            _logger.Info("RunFtpUpload completed");
        }

        private void RunBuildCleanUp(ServiceOptionsRoot serviceOptions, Dictionary<IDynamicSourceDetails, BuildPackageInformation> buildQueryManifest)
        {
            ///////////////////////////////////////////////////////////
            // Clean up
            ///////////////////////////////////////////////////////////
            try
            {

                foreach (var buildUpdateItem in buildQueryManifest)
                {
                    var currentTarget = buildUpdateItem;

                    _logger.Info("Cleanning cache: " + currentTarget.Value.Branch);
                    
                    // Collect the data on the items that were cleaned, so that the DB status can be set
                    var packageDeployedInfo = CleanUpCacheShare(serviceOptions, _logger, currentTarget.Key);

                    foreach (var deployedPackageInfo in packageDeployedInfo)
                    {
                        _tfsOps.SetDbDeployedStatus(deployedPackageInfo.Project, deployedPackageInfo.Branch,
                            deployedPackageInfo.SubBranch,
                            deployedPackageInfo.Version, deployedPackageInfo.Deployed);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.InfoException("Exception Cleaning Cache Server", ex);
            }
        }

        /// <summary>
        /// Acquires and builds up a local cache of packages
        /// </summary>
        /// <param name="serviceOptions">ServiceOptionsRoot - json configuration options</param>
        /// <param name="buildQueryManifest">Dictionary<IDynamicSourceDetails, BuildPackageInformation> used to loop through the target builds</param>
        /// <param name="buildUpdateManifest">List<KeyValuePair<string, IDynamicSourceDetails>></param>
        private void RunBuildUpdate(ServiceOptionsRoot serviceOptions, Dictionary<IDynamicSourceDetails, BuildPackageInformation> buildQueryManifest, List<KeyValuePair<string, IDynamicSourceDetails>> buildUpdateManifest)
        {
            _logger.Info("**== RunBuildUpdate started ==**");
            LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(4);
            TaskFactory factory = new TaskFactory(lcts);
            List<Task> tasks = new List<Task>();

            foreach (var buildUpdateItem in buildQueryManifest)
            {
                var currentTarget = buildUpdateItem;
                var buildInformation = currentTarget.Value;

                Task t =
                    factory.StartNew(
                        () =>
                            AcquirePackageFromDrop(serviceOptions, currentTarget, buildInformation,
                                buildUpdateManifest),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning, lcts);
                tasks.Add(t);
            }
            _logger.Info("RunBuildUpdate total tasks: " + tasks.Count());

            Task.WaitAll(tasks.ToArray());

            /////////////////////////////////////////////////////////
            // Handle the individual returns synchronously to update
            // the Database 
            /////////////////////////////////////////////////////////
            foreach (var task in tasks)
            {
                var task1 = task as Task<ConcurrentBag<DeployedPackageInfo>>;
                if (task1 != null && task1.Result.Any())
                {
                    foreach (var deployedPackageInfo in task1.Result)
                    {
                        _tfsOps.SetDbDeployedStatus(deployedPackageInfo.Project, deployedPackageInfo.Branch,
                            deployedPackageInfo.SubBranch,
                            deployedPackageInfo.Version, deployedPackageInfo.Deployed);
                    }
                }
            }

            _logger.Info("**== RunBuildUpdate Completed ==**");
        }


        /// <summary>
        /// Build up the list of branches/source/builds to process
        /// </summary>
        /// <param name="serviceOptions">ServiceOptionsRoot - json configuration options</param>
        /// <param name="buildQueryManifest">Dictionary<IDynamicSourceDetails, BuildPackageInformation> </param>
        private void ComputeUpdateData(ServiceOptionsRoot serviceOptions, ref Dictionary<IDynamicSourceDetails, BuildPackageInformation> buildQueryManifest)
        {
            // Compute all build queries up front....
            
            _logger.Info("********************************************************************");
            _logger.Info("Computing build information");
            _logger.Info("********************************************************************");
            // Used the options file to query for the list of branches/projects to operate on
            var branchList = _tfsOps.GetBranchList(serviceOptions.BuildsToWatch.ToList<IBuildsToWatch>());

            _logger.Info("TFS Branches Found: {0}", branchList.Count);
            foreach (var branch in branchList)
            {
                _logger.Info(branch);
            }

            // Validate that the buidls exist and put together the data needed to acquire them
            var validateAndRefineBuildsToWatch = _tfsOps.ValidateAndRefineBuildsToWatch(branchList,
                serviceOptions.BuildsToWatch.ToList<IBuildsToWatch>());

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
                    _logger.Info("Adding build to watch to Queue: {0} - {1}, VERSION: {2}",
                        dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch,
                        buildInformation.GetDeploymentVersionNumber());
                    lock (_thisLock)
                    {
                        buildQueryManifest.Add(dynamicSourceDetails.Key, buildInformation);
                    }
                }
                else
                {
                    _logger.Error("Skipping addition of build to the queue because Query failed: {0} - {1}",
                        dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch);
                }
            }
            
        }

        /// <summary>
        /// orchestrates to acquisition of the packages that make up a build/install package
        /// </summary>
        /// <param name="serviceOptions"></param>
        /// <param name="buildsToWatch"></param>
        /// <param name="buildInformation"></param>
        /// <param name="buildUpdateManifest"></param>
        /// <returns></returns>
        private ConcurrentBag<DeployedPackageInfo> AcquirePackageFromDrop(ServiceOptionsRoot serviceOptions,
            KeyValuePair<IDynamicSourceDetails, BuildPackageInformation> buildsToWatch,
            BuildPackageInformation buildInformation,
            List<KeyValuePair<string, IDynamicSourceDetails>> buildUpdateManifest)
        {

            ConcurrentBag<DeployedPackageInfo> packageDeployedInfo = new ConcurrentBag<DeployedPackageInfo>();
            _logger.Info("Acquired build information from TFS server");
            var strPath = FileUtils.GetFullFormatedPathForBuild(serviceOptions.StagingFileShare,
                buildsToWatch.Key, buildInformation);
            Guid g = Guid.NewGuid();

            _logger.Info("XCopy starting: " + strPath + " :: " + g);
            var count = FileUtils.XCopyFilesParallel(strPath, buildInformation.InstallationComponents, _logger);
            var expectedCnt = buildInformation.InstallationComponents.Count;
            if (count != expectedCnt)
            {
                _logger.Error(
                    "ERROR: XCopy might have failed to copy all required components!  Expected: {0} - Copied {1}",
                    expectedCnt, count);
            }
            _logger.Info("XCopy completed: " + strPath + " :: " + g);

            // write Manifest out
            var manifestPath = Path.Combine(strPath, "BuildMainifest.json");
            File.WriteAllText(manifestPath, buildInformation.BuildManifest);

            // Add this item to the manifest for easy process later down the line.
            buildUpdateManifest.Add(new KeyValuePair<string, IDynamicSourceDetails>(strPath, buildsToWatch.Key));

            var verNumber = buildInformation.GetDeploymentVersionNumber();

            // Save info to update the database.
            DeployedPackageInfo deployedPackageInfo = new DeployedPackageInfo()
            {
                Project = buildsToWatch.Key.Project,
                Branch = buildsToWatch.Key.Branch,
                Deployed = true,
                SubBranch = buildsToWatch.Key.SubBranch,
                Version = verNumber
            };

            // TODO - Process synchronously
            //try
            //{
            //    if (serviceOptions.RunCheckSum)
            //    {
            //        _logger.Info("Generating checksum: " + strPath);
            //        foreach (var file in Directory.GetFiles(strPath, "*.exe", SearchOption.TopDirectoryOnly))
            //        {
            //            _tfsOps.UpdateCheckSum(buildsToWatch.Key.Project, buildsToWatch.Key.Branch,
            //                buildsToWatch.Key.SubBranch,
            //                verNumber, file);
            //        }
            //        _logger.Info("Generating checksum complete: " + strPath);
            //    }
            //}
            //catch (Exception)
            //{
            //    // ignored
            //}



            var tickCount = Environment.TickCount;
            _logger.Info("Completed work: Task= " + Task.CurrentId + " ,TickCount= " + tickCount + " ,Thread= " +
                         Thread.CurrentThread.ManagedThreadId);

            // Add the newly deployed item to the collection so that the DB record can be updated.
            packageDeployedInfo.Add(deployedPackageInfo);
            return packageDeployedInfo;
        }

        
        /// <summary>
        /// Cleans up packages/directories that fall outside of the retention scope
        /// </summary>
        /// <param name="serviceOptions"></param>
        /// <param name="logger"></param>
        /// <param name="buildsToWatch"></param>
        /// <returns></returns>
        private ConcurrentBag<DeployedPackageInfo> CleanUpCacheShare(ServiceOptionsRoot serviceOptions, Logger logger,
            IDynamicSourceDetails buildsToWatch)
        {
            ConcurrentBag<DeployedPackageInfo> deployedPackageInfos = new ConcurrentBag<DeployedPackageInfo>();

            string subPath = String.Empty;
            // RUN LOCAL CLEAN UP PROCESS
            if (String.CompareOrdinal(buildsToWatch.SubBranch, "$/" + buildsToWatch.Project + "/" + buildsToWatch.Branch) == 0)
            {
                subPath = buildsToWatch.Branch;
            }
            else
            {
                subPath = buildsToWatch.SubBranch;
            }

            var directoryToClean = FileUtils.GetFormatedPathForBuild(serviceOptions.StagingFileShare, subPath);
            logger.Info("Attempting to clean: " + directoryToClean);
            var cleanUpDirectories =
                FileUtils.CleanUpDirectories(directoryToClean, buildsToWatch.Retention, logger);

            foreach (var cleanUpDirectory in cleanUpDirectories)
            {
                logger.Info("Removed Directory: {0}", cleanUpDirectory);
                // Need to do something better with Project name and maybe mark status as removed or clean up the record.              

                DeployedPackageInfo deployedPackageInfo = new DeployedPackageInfo()
                {
                    Project = buildsToWatch.Project,
                    Branch = buildsToWatch.Branch,
                    Deployed = false,
                    SubBranch = buildsToWatch.SubBranch,
                    Version = cleanUpDirectory.Value
                };

                deployedPackageInfos.Add(deployedPackageInfo);
            }
            logger.Info("Cleaning local cache complete");

            return deployedPackageInfos;
        }

        /// <summary>
        /// Cleans up old log files
        /// </summary>
        /// <param name="logDirPath"></param>
        /// <param name="retention"></param>
        /// <param name="logger"></param>
        private void CleanUpLogDir(string logDirPath, int retention, Logger logger)
        {
            logger.Info("Cleaning log directory");
            // RUN LOCAL CLEAN UP PROCESS
            FileUtils.CleanUpFiles(logDirPath, retention, logger);

            logger.Info("Cleaning log directory complete");
        }


        /// <summary>
        /// Temp object to store deployment data 
        /// </summary>
        public class DeployedPackageInfo
        {
            public string Project { get; set; }
            public string Branch { get; set; }
            public string SubBranch { get; set; }
            public string Version { get; set; }
            public bool Deployed { get; set; }

        }
    }
}
