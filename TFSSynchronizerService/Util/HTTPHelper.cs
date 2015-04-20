using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildDataDriver.tools;

namespace FTIPusher.Util
{
    public class HTTPHelper
    {
        private readonly ServiceCoreLogic _serviceCoreLogic;

        public HTTPHelper(ServiceCoreLogic serviceCoreLogic)
        {
            _serviceCoreLogic = serviceCoreLogic;
        }

        /// <summary>
        /// Core loop for HTTP/REST operations
        /// </summary>
        /// <param name="serviceOptions"></param>
        /// <param name="directoryTopLevel"></param>
        public void RunCoreHTTPSync(ServiceOptionsRoot serviceOptions, DirectoryInfo[] directoryTopLevel)
        {
            foreach (var mirrorLocation in serviceOptions.HTTPShares)
            {
                RestUploadManager restUploadManager = new RestUploadManager(mirrorLocation, serviceOptions.RESTAPIVER);
                _serviceCoreLogic.Logger.Info("HTTP Mirror location: {0}", mirrorLocation);

                try
                {
                    // First wipe out an extra directories that exist only on the HTTP side
                    var ret = restUploadManager.GetBranchDirectoryNames();
                    ret.Wait();

                    _serviceCoreLogic.Logger.Info("HTTP Mirror Found existing branches: {0}", ret.Result.Count());

                    // Get the differences
                    var remoteRemove = from obj in ret.Result
                        where directoryTopLevel.All(dir => dir.Name != obj)
                        select obj;

                    _serviceCoreLogic.Logger.Info("HTTP Mirror Found {0} existing branches to remove.", remoteRemove.Count());
                    foreach (var remoteDirectory in remoteRemove)
                    {
                        try
                        {
                            _serviceCoreLogic.Logger.Info("HTTP Mirror removing remote folder: {0}", remoteDirectory);
                            restUploadManager.DeleteDirectory(remoteDirectory).Wait();
                        }
                        catch (Exception exception)
                        {
                            _serviceCoreLogic.Logger.ErrorException("Exception removing remote directory via HTTP: " + remoteDirectory, exception);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _serviceCoreLogic.Logger.ErrorException("Exception querrying for remote branches to clean via HTTP", exception);
                }


                // Query and Upload - Sync folders from cache share to the HTTP Server
                // Foreach top level ( BRANCH ) directory get a listing of the sub directories ( VERSION ) 
                // and compare that to what we have on the HTTP side.  Upload the missing folders
                foreach (var topLevelDirInfo in directoryTopLevel)
                {
                    DirectoryInfo[] versionSubDirs = new DirectoryInfo[] {};

                    try
                    {
                        versionSubDirs = topLevelDirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception exception)
                    {
                        _serviceCoreLogic.Logger.ErrorException("Exception getting directory list from: " + topLevelDirInfo.FullName, exception);
                    }


                    var branchQuery = restUploadManager.GetVersionDirectoryNames(topLevelDirInfo.Name);
                    branchQuery.Wait();

                    // Remote extra folder in local cache share
                    var missingRemoteItems = from dir in versionSubDirs
                        where branchQuery.Result.All(folder => folder != dir.Name)
                        select dir;

                    CalulateFilesAndUpload(missingRemoteItems, restUploadManager);
                }
            }
        }

        private void CalulateFilesAndUpload(IEnumerable<DirectoryInfo> missingRemoteItems, RestUploadManager restUploadManager)
        {
            LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(4);
            TaskFactory factory = new TaskFactory(lcts);
            List<Task> tasks = new List<Task>();

            foreach (var missingRemoteItem in missingRemoteItems)
            {
                string[] files = new string[] {};
                try
                {
                    files = Directory.GetFiles(missingRemoteItem.FullName, "*.exe", SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception)
                {
                    _serviceCoreLogic.Logger.ErrorException("Exception getting file list from: " + missingRemoteItem.FullName,
                        exception);
                }
                var localMissingRemote = missingRemoteItem;
                //GetValue(restUploadManager, files, missingRemoteItem);
                Task t = factory.StartNew(() => PerformFileUpload(restUploadManager, files, localMissingRemote),
                               CancellationToken.None,
                               TaskCreationOptions.LongRunning, lcts);
                tasks.Add(t);
            }

            Task.WaitAll(tasks.ToArray());
        }

        private void PerformFileUpload(RestUploadManager restUploadManager, string[] files, DirectoryInfo missingRemoteItem)
        {
            foreach (var file in files)
            {
                var remotePath = missingRemoteItem.Parent + "/" + missingRemoteItem.Name;
                _serviceCoreLogic.Logger.Info("HTTP Mirror uploading file: {0} - to: {1}", file, remotePath);
                try
                {
                    restUploadManager.Uploader(file, remotePath);
                }
                catch (Exception exception)
                {
                    _serviceCoreLogic.Logger.ErrorException(
                        "Exception uploading file via REST: " + file + " - remote: " + remotePath, exception);
                }
            }
        }
    }
}