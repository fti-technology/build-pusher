using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using NLog;
using WinSCP;

namespace BuildDataDriver.tools
{
    /// <summary>
    /// Wrap up WinSCP for FTP Operations
    /// </summary>
    public class FtpOperations
    {
        
        public class UploadException : Exception
        {
            public UploadException(string project)
                : base(project)
            {
            }
        }

        private readonly string _password;
        private readonly string _userName;
        private readonly string _hostName;
        private readonly string _proxyHost;
        private readonly string _proxyPort;

        private readonly Logger _logger;

        public FtpOperations(string userName, string password, string hostName, Logger Logger)
            : this(userName, password, hostName, null, null, Logger)
        {
        }

        public FtpOperations(string userName, string password, string hostName, string proxyHost, string proxyPort, Logger Logger)
        {
            _password = password;
            _userName = userName;
            _hostName = hostName;
            _proxyHost = proxyHost;
            _proxyPort = proxyPort;
            _logger = Logger;
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="project"></param>
        ///// <param name="sourcePath"></param>
        ///// <param name="branch"></param>
        public void UploadFiles(string sourcePath, string destPath)
        {
           
            IEnumerable<string> fileList = Directory.GetFiles(sourcePath, "*.*");

            Parallel.ForEach(fileList, currentFilePath =>
            {
                using (var session = new Session())
                {
                    try
                    {
                        session.Open(GetSessionOptions());

                        var result = session.PutFiles(currentFilePath, destPath);

                        if (result.IsSuccess)
                        {
                            _logger.Info("Successfully transfered file from: {0} to {1}", currentFilePath, destPath);
                        }
                        else
                        {
                            _logger.Error("Failed to upload file {0}", currentFilePath);
                        }
                    }
                    catch (Exception ex)
                    {

                        _logger.InfoException("Exception uploading file to ftp", ex);
                    }
                }
            });

           
        }

        /// <summary>
        /// Create Remote directory on FTP
        /// </summary>
        /// <param name="path"></param>
        public void CreateRemoteDir(string path)
        {
            using (Session session = new Session())
            {
                // Connect
                session.Open(GetSessionOptions());
                
                try
                {
                    session.ListDirectory(path);
                }
                catch (Exception ex)
                {
                    session.CreateDirectory(path);
                    _logger.Info("Created Remote FTP Directory: {0}", path);
                }
            }
        }

        /// <summary>
        /// Create Remote directory on FTP
        /// </summary>
        /// <param name="path"></param>
        private void CreateRemoteDir(string path, Session session)
        {
                // Connect
                if(!session.Opened)
                    session.Open(GetSessionOptions());
                try
                {
                    session.ListDirectory(path);
                }
                catch (Exception ex)
                {
                    session.CreateDirectory(path);
                    _logger.Info("Created Remote FTP Directory: {0}", path);
                }
        }

        private List<string> GetRemoteDirList(string path, Session session)
        {
            List<string> list = new List<string>();

            // Connect
            if (!session.Opened)
            {
                session.Open(GetSessionOptions());
            }

            RemoteDirectoryInfo directory = null;

            try
            {
                directory = session.ListDirectory(path);
            }
            catch (Exception){}

            if (directory == null)
                return list;

            foreach (RemoteFileInfo fileInfo in directory.Files)
            {
                if (fileInfo.IsDirectory)
                {
                    if (String.CompareOrdinal(fileInfo.Name, "..") != 0)
                    {
                        list.Add(VirtualPathUtility.Combine(path, fileInfo.Name));
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Logging for Mirror operations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                _logger.Info("Upload of {0} succeeded", e.FileName);
            }
            else
            {
                _logger.Info("Upload of {0} failed: {1}", e.FileName, e.Error);
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    _logger.Info("Permisions of {0} set to {1}", e.Chmod.FileName, e.Chmod.FilePermissions);
                }
                else
                {
                    _logger.Info("Setting permissions of {0} failed: {1}", e.Chmod.FileName, e.Chmod.Error);
                }
            }
            else
            {
                _logger.Info("Permissions of {0} kept with their defaults", e.Destination);
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    _logger.Info("Timestamp of {0} set to {1}", e.Touch.FileName, e.Touch.LastWriteTime);
                }
                else
                {
                    _logger.Info("Setting timestamp of {0} failed: {1}", e.Touch.FileName, e.Touch.Error);
                }
            }
            else
            {
                // This should never happen with Session.SynchronizeDirectories
                _logger.Info("Timestamp of {0} kept with its default (current time)", e.Destination);
            }
        }

        public void MirrorDirectory(string source, string dest, string ServerName)
        {
            MirrorDirectory(source, dest, string.Empty, ServerName);
        }

        /// <summary>
        /// Mirror local directory to remote FTP destination
        /// </summary>
        /// <param name="source"></param>
        /// <param name="dest"></param>
        public void MirrorDirectory(string source, string dest, string sourceRoot, string ServerName)
        {
                                                    
            DirectoryInfo dSource = new DirectoryInfo(source);
            DirectoryInfo dDest = new DirectoryInfo(dest);  

            if(!String.IsNullOrEmpty(sourceRoot))
                dest = dSource.ToString().Replace(sourceRoot, dDest.ToString());
            else
            {
                dest = dDest.ToString();
            }

            dest = dest.Replace("\\", "//");
            dest = dest.Replace("//", "/");

            try
            {
                CreateRemoteDir(dest);
                
            }
            catch (Exception ex)
            {

                _logger.InfoException("Exception Creating remote ftp path : " + dest, ex);
            }

            using (Session session = new Session())
            {
                // Will continuously report progress of synchronization
                session.FileTransferred += FileTransferred;

                // Connect
                try
                {
                    session.Open(GetSessionOptions());
                }
                catch (Exception exception)
                {

                    _logger.InfoException("Could not create FTP session.", exception);
                }
                

                if (session.Opened)
                {
                    var guid = Guid.NewGuid();

                    _logger.Info("FTP UPLOAD START - SERVER $$$ {0} $$ {1} $$ Source: {2} - Dest: {3}", ServerName, guid, source, dest);
                    // Synchronize files
                    Console.WriteLine(source);
                    Console.WriteLine(dest);
                    
                    SynchronizationResult synchronizationResult =
                        session.SynchronizeDirectories(SynchronizationMode.Remote, source, dest, true, true, SynchronizationCriteria.Either, new TransferOptions { TransferMode = TransferMode.Automatic, PreserveTimestamp = true});
                    _logger.Info("FTP UPLOAD COMPLETE - SERVER $$$ {0} $$ {1} $$ Source: {2} - Dest: {3}", ServerName, guid, source, dest);

                    try
                    {
                        _logger.Info("synchronizationResult {0} - Removals: ", guid);
                        try
                        {
                            foreach (var removal in synchronizationResult.Removals)
                            {
                                    _logger.Info("Removed {0} - Guid: ", removal, guid);
                                
                            }
                        }
                        catch (Exception){}

                        _logger.Info("synchronizationResult {0} - Uploads: ", guid);
                        try
                        {
                            foreach (var upload in synchronizationResult.Uploads)
                            {
                                _logger.Info("Upload {0} - Guid: ", upload, guid);

                            }
                        }
                        catch (Exception) { }


                    }
                    catch (Exception exception)
                    {
                        _logger.ErrorException("FTP EXCEPTION  $$$ " + ServerName + " $$ " + guid + " $$", exception);
                    }
                }
            }
        }

        // Setup the WinSCP options
        private SessionOptions GetSessionOptions()
        {
            var sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Ftp,
                HostName = _hostName,
                UserName = _userName,
                Password = _password,
                Timeout = new TimeSpan(0,0,4,0)
            };

            if (!string.IsNullOrEmpty(_proxyHost))
            {
                sessionOptions.AddRawSettings("ProxyMethod", "3");
                sessionOptions.AddRawSettings("ProxyHost", _proxyHost);
                sessionOptions.AddRawSettings("ProxyPort", _proxyPort);
            }

            return sessionOptions;
        }
    }
}


