using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BuildDataDriver.Interfaces;
using Microsoft.TeamFoundation.Framework.Client;
using NLog;

namespace BuildDataDriver.tools
{
    public class FileUtils
    {

        /// <summary>
        /// Path in form of \\TESTSHARE\Builds\2015\20150223.3
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="dynamicSourceDetails"></param>
        /// <param name="buildInformation"></param>
        /// <returns></returns>
        //public static string GetFullFormatedPathForBuild(string dest, string branch, IBuildPackageInformation buildInformation)

        public static string GetFullFormatedPathForBuild(string dest, IDynamicSourceDetails dynamicSourceDetails, IBuildPackageInformation buildInformation)
        {
            //dest/branch/version
            var version = buildInformation.GetDeploymentVersionNumber();

            var branchTarget = String.CompareOrdinal(dynamicSourceDetails.SubBranch,
                "$/" + dynamicSourceDetails.Project + "/" + dynamicSourceDetails.Branch) == 0 ? dynamicSourceDetails.Branch : dynamicSourceDetails.SubBranch;

            string[] paths = new string[] { dest, branchTarget, version };
            
            return Path.Combine(paths);
        }

        /// <summary>
        /// Path in form of \\TESTSHARE\Builds\2015
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="branch"></param>
        /// <param name="subBranch"></param>
        /// <returns></returns>
        public static string GetFormatedPathForBuild(string dest, string branch, string subBranch)
        {

            var branchTarget = String.CompareOrdinal(subBranch,
                "$/" + branch + "/" + subBranch) == 0 ? branch : subBranch;

            var paths = new string[] { dest, branchTarget };
            return Path.Combine(paths);
        }

        /// <summary>
        /// Path in form of \\TESTSHARE\Builds\2015
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="branchAndsubBranch"></param>        
        /// <returns></returns>
        public static string GetFormatedPathForBuild(string dest, string branchAndsubBranch)
        {
            //dest/branch
            string[] paths = { dest, branchAndsubBranch };
            return Path.Combine(paths);
        }

        public static string ComputeMd5(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        return Convert.ToBase64String(md5.ComputeHash(stream));
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="logger"></param>
        public static void CreateDestDirectory(string path, Logger logger)
        {
            try
            {
                // Determine whether the directory exists. 
                if (Directory.Exists(path))
                {
                    return;
                }

                // Try to create the directory.
                DirectoryInfo di = Directory.CreateDirectory(path);
                logger.Info("The directory was created successfully at {0}.", Directory.GetCreationTime(path));
            }
            catch (Exception e)
            {
                logger.Error("CreateDestDirectory failed for path {0} - exception: {1}",path, e.Message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="fileList"></param>
        /// <param name="logger"></param>
        public static void CopyFiles(string dest, IEnumerable<string> fileList, Logger logger)
        {
            foreach (var file in fileList)
            {
                try
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    File.Copy(file, Path.Combine(dest, fileName), true);
                }
                catch (IOException copyError)
                {
                    logger.Error("CopyFiles failed - exception: {1}", copyError.Message);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="fileList"></param>
        /// <param name="logger"></param>
        public static void XCopyFiles(string dest, Dictionary<string, KeyValuePair<string, string>> fileList, Logger logger)
        {
            try
            {
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(dest);
                    logger.Info("Directory created: {0}", dest);
                }
            }
            catch (Exception ex)
            {
                // ignored
                logger.InfoException("XCopyFilesParallel failed: SOURCE: " + dest, ex);
            }

            foreach (var file in fileList)
            {
                RunXCopyProcess(dest, logger, file.Value.Value);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dest"></param>
        /// <param name="fileList"></param>
        /// <param name="logger"></param>
        public static int XCopyFilesParallel(string dest,Dictionary<string,KeyValuePair<string,string>> fileList, Logger logger)
        {
            var count = 0;
            try
            {
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(dest);
                    logger.Info("Directory created: {0}", dest);
                }
            }
            catch (Exception ex)
            {
                // ignored
                logger.InfoException("XCopyFilesParallel failed: SOURCE: " + dest, ex);
            }

            var total = fileList.Count;
            Parallel.ForEach(fileList, file =>
            {
                logger.Info("XCopy file: " + file + " dest: " + dest + "  count: " + count + "/" + total);
                RunXCopyProcess(dest, logger, file.Value.Value);
                Interlocked.Increment(ref count);
            });
            logger.Info("XCopy file completed count: " + count + "/" + total);
            return count;
        }

        public static int XCopyFilesParallel(string dest, Dictionary<string, IInstallDetail> fileList, Logger logger)
        {
            var count = 0;
            try
            {
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(dest);
                    logger.Info("Directory created: {0}", dest);
                }
            }
            catch (Exception ex)
            {
                // ignored
                logger.InfoException("XCopyFilesParallel failed: SOURCE: " + dest, ex);
            }

            var total = fileList.Count;
            Parallel.ForEach(fileList.Values, file =>
                {
                    var copyTarget = file.InstallDetails.Value;
                logger.Info("XCopy file: " + copyTarget + " dest: " + dest + "  count: " + count + "/" + total);
                RunXCopyProcess(dest, logger, copyTarget);
                Interlocked.Increment(ref count);
            });
            logger.Info("XCopy file completed count: " + count + "/" + total);
            return count;
        }

        private static void RunXCopyProcess(string dest, Logger logger, string file)
        {
            const string processExe = "xcopy.exe";
            var source = file;
            string processCommandLine = "\"" + source + "\" " + "\"" + dest + "\"" + " /j /c /y";

            var process = ProcessRunner.RunProcess(processExe, processCommandLine);

            if (process.HasExited)
            {
                logger.Info("Xcopy process has exited.");
                string message = "Unknown";
                if (process.ExitCode == 0)
                {
                    message = "Files were copied sucessfully.";
                    logger.Info("XCopy: {0}. SOURCE: {1} - DEST: {2} ", message, source, dest);
                }
                else
                {
                    switch (process.ExitCode)
                    {
                        case 1:
                            message = "No files were found to copy";
                            break;
                        case 2:
                            message = "The user pressed CTRL+C to terminate xcopy";
                            break;
                        case 4:
                            message =
                                "Initialization error occurred. There is not enough memory or disk space, or you entered an invalid drive name or invalid syntax on the command line.";
                            break;
                        case 5:
                            message = "Disk write error occurred.";
                            break;
                        default:
                            message =
                                "Unknown return code { " + process.ExitCode  + " }" ;
                            break;
                    }
                    logger.Error("XCopy: {0}. SOURCE: {1} - DEST: {2} ", message, source, dest);
                }
            }
            else
            {
                logger.Error("XCopyFiles failed to exit : SOURCE: {0} - Dest {1}", source, dest);
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            try
            {
                // force clean-up
                process.Close();
            }
            catch (Exception)
            {
                // ignored
            }

            //logger.Info("XCopy complete, SOURCE: {0} - Dest {1}", file.Value.Value, dest);
        }


        public static List<KeyValuePair<string,string>> CleanUpDirectories(string path, int numberOfDirectoriesToKeep, Logger logger)
        {
            List<KeyValuePair<string,string>> removedItesmList = new List<KeyValuePair<string, string>>();
            if (!Directory.Exists(path))
                return removedItesmList;

            logger.Info("Running directory clean-up process on {0}", path);

            var cnt = Directory.GetDirectories(path).Count();
            if (cnt <= numberOfDirectoriesToKeep)
            {
                logger.Info("No directories to clean");
                return removedItesmList;
            }
            else
            {
                
            }

            cnt = cnt - numberOfDirectoriesToKeep;

            var orderByDescending =
                    Directory.GetDirectories(path)
                        .OrderByDescending(d => new DirectoryInfo(d).CreationTime)
                        .Reverse()
                        .Take(cnt);

            foreach (var dirToRemove in orderByDescending)
            {
                try
                {
                    var parentDirName = Directory.GetParent(dirToRemove).Name;
                    var directory = Path.GetDirectoryName(dirToRemove);
                    Directory.Delete(dirToRemove, true);
                    removedItesmList.Add(new KeyValuePair<string, string>(parentDirName, directory));

                    logger.Info("Removed directory: {0}", dirToRemove);
                }
                catch (Exception ex)
                {
                    logger.ErrorException("Exception, Removing directory: " + dirToRemove, ex);
                }
            }

            return removedItesmList;
        }

        public static IEnumerable<string> CleanUpFiles(string path, int numberOfFilesToKeep, Logger logger)
        {
            List<string> removedFilesList = new List<string>();
            
            int cnt = Directory.GetFiles(path, "*.log").Count();
            if (cnt <= numberOfFilesToKeep)
            {
                logger.Info("No Log Files to clean");
                return removedFilesList;
            }

            cnt = cnt - numberOfFilesToKeep;

            var orderByDescending =
                 Directory.GetFiles(path,"*.log")
                     .OrderByDescending(d => new FileInfo(d).CreationTime)
                     .Reverse()
                     .Take(cnt);

            foreach (var fileToRemove in orderByDescending)
            {
                try
                {
                    File.Delete(fileToRemove);
                    removedFilesList.Add(fileToRemove);
                    logger.Info("Removed file: {0}", fileToRemove);
                }
                catch (Exception ex)
                {
                    logger.ErrorException("Exception, Removing file: " + fileToRemove, ex);
                }
            }

            return removedFilesList;
        }
   
        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(new Uri(path).LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }


        public static void RoboCopyFile(string source, string dest)
        {
            const string exeName = "robocopy.exe";


            var filename = System.IO.Path.GetFileName(source);
            var sourceDir = System.IO.Path.GetDirectoryName(source);

            string commandLine = string.Format("\"{0}\" \"{1}\" \"{2}\" /W:5 /R:{3} /MT:4 /FFT /IPG:0", sourceDir, dest, filename, 4);
            
            Process process = ProcessRunner.RunProcess(exeName, commandLine);

            if (process.HasExited)
            {
                Debug.WriteLine("Robocopy process has exited,  SOURCE: {0} - Dest {1}", source, dest);

                switch (process.ExitCode)
                {
                    case 16: Debug.WriteLine("MirrorDirectory: Fatal Error ");
                        break;
                    case 15: Debug.WriteLine("MirrorDirectory: OKCOPY + FAIL + MISMATCHES + XTRA  ");
                        break;
                    case 14: Debug.WriteLine("MirrorDirectory: FAIL + MISMATCHES + XTRA ");
                        break;
                    case 13: Debug.WriteLine("MirrorDirectory: OKCOPY + FAIL + MISMATCHES ");
                        break;
                    case 12: Debug.WriteLine("MirrorDirectory: FAIL + MISMATCHES ");
                        break;
                    case 11: Debug.WriteLine("MirrorDirectory: OKCOPY + FAIL + XTRA ");
                        break;
                    case 10: Debug.WriteLine("MirrorDirectory: FAIL + XTRA ");
                        break;
                    case 9: Debug.WriteLine("MirrorDirectory: OKCOPY + FAIL ");
                        break;
                    case 8: Debug.WriteLine("MirrorDirectory: FAIL ");
                        break;
                    case 7: Debug.WriteLine("MirrorDirectory: OKCOPY + MISMATCHES + XTRA  ");
                        break;
                    case 6: Debug.WriteLine("MirrorDirectory: MISMATCHES + XTRA ");
                        break;
                    case 5: Debug.WriteLine("MirrorDirectory: OKCOPY + MISMATCHES  ");
                        break;
                    case 4: Debug.WriteLine("MirrorDirectory: MISMATCHES ");
                        break;
                    case 3: Debug.WriteLine("MirrorDirectory: OKCOPY + XTRA ");
                        break;
                    case 2: Debug.WriteLine("MirrorDirectory: XTRA ");
                        break;
                    case 1: Debug.WriteLine("MirrorDirectory: OKCOPY ");
                        break;
                    case 0: Debug.WriteLine("MirrorDirectory: No Change ");
                        break;
                }
            }
            else
            {
                Debug.WriteLine("Robocopy process has failed to exit,  SOURCE: {0} - Dest {1}", source, dest);
                try
                {
                    process.Kill();
                }
                catch (Exception) { }
            }

            try
            {
                process.Close();
            }
            catch (Exception) { }

        }

        public static void MirrorDirectory(string source, string dest, Logger logger, bool createRootPath, string logDir = null)
        {
            MirrorDirectory(source, dest, logger, 0, createRootPath, logDir);
        }

        public static void MirrorDirectory(string source, string dest, Logger logger, int failedRetryNumber, bool createRootPath, string logDir = null)
        {
            const string exeName = "robocopy.exe";
            //robocopy "E:\test" \\server\public\test\ /MIR /W:20 /R:15 /LOG: \\server\public\logs 
            if (!Directory.Exists(dest))
            {
                try
                {
                    Directory.CreateDirectory(dest);
                    logger.Info("Created directory: {0}", dest);
                }
                catch (Exception ex)
                {
                    logger.InfoException("Exception Creating directory : " + dest, ex);
                }
            }

            if (createRootPath)
            {
                // Check if paths end in same directory since robocopy doesn't copy the root/end of the path
                if (Path.GetFileName(NormalizePath(source)) != Path.GetFileName(NormalizePath(dest)))
                {

                    try
                    {
                        dest = Path.Combine(dest, Path.GetFileName(NormalizePath(source)));
                        Directory.CreateDirectory(dest);
                    }
                    catch (Exception ex)
                    {
                        logger.InfoException("Exception Creating directory : " + dest, ex);
                    }
                }
            }

            // just default to something reasonable
            if (failedRetryNumber <= 0)
                failedRetryNumber = 10;

            string commandLine = string.Format("\"{0}\" \"{1}\" /MIR /W:5 /R:{2} /MT:4 /FFT /IPG:0", source, dest, failedRetryNumber);
            string logFileName = string.Empty;
            if (!string.IsNullOrEmpty(logDir))
            {
                string n = string.Format("text-{0:yyyy-MM-dd_hh-mm-ss-tt}.log", DateTime.Now);
                logFileName = dest;
                logFileName = logFileName.Replace("\\", "_");
                logFileName = logFileName + "-" + n;
                logFileName = Path.Combine(logDir, logFileName);

                commandLine = string.Format("{0} /LOG:\"{1}\" /TS /NP /TEE", commandLine, logFileName);
            }

            logger.Info("Robocopy log: {0}", logFileName);
            Process process = ProcessRunner.RunProcess(exeName, commandLine);

            if (process.HasExited)
            {
                logger.Info("Robocopy process has exited,  SOURCE: {0} - Dest {1}", source, dest);
                
                switch (process.ExitCode)
                {
                    case 16: logger.Error("MirrorDirectory: Fatal Error ");
                        break;
                    case 15: logger.Warn("MirrorDirectory: OKCOPY + FAIL + MISMATCHES + XTRA  ");
                        break;
                    case 14: logger.Warn("MirrorDirectory: FAIL + MISMATCHES + XTRA ");
                        break;
                    case 13: logger.Warn("MirrorDirectory: OKCOPY + FAIL + MISMATCHES ");
                        break;
                    case 12: logger.Warn("MirrorDirectory: FAIL + MISMATCHES ");
                        break;
                    case 11: logger.Warn("MirrorDirectory: OKCOPY + FAIL + XTRA ");
                        break;
                    case 10: logger.Warn("MirrorDirectory: FAIL + XTRA ");
                        break;
                    case 9: logger.Warn("MirrorDirectory: OKCOPY + FAIL ");
                        break;
                    case 8: logger.Error("MirrorDirectory: FAIL ");
                        break;
                    case 7: logger.Warn("MirrorDirectory: OKCOPY + MISMATCHES + XTRA  ");
                        break;
                    case 6: logger.Warn("MirrorDirectory: MISMATCHES + XTRA ");
                        break;
                    case 5: logger.Info("MirrorDirectory: OKCOPY + MISMATCHES  ");
                        break;
                    case 4: logger.Info("MirrorDirectory: MISMATCHES ");
                        break;
                    case 3: logger.Info("MirrorDirectory: OKCOPY + XTRA ");
                        break;
                    case 2: logger.Info("MirrorDirectory: XTRA ");
                        break;
                    case 1: logger.Info("MirrorDirectory: OKCOPY ");
                        break;
                    case 0: logger.Info("MirrorDirectory: No Change ");
                        break;
                }       
            }
            else
            {
                logger.Info("Robocopy process has failed to exit,  SOURCE: {0} - Dest {1}", source, dest);
                try
                {
                    process.Kill();
                }
                catch (Exception){}
            }

            try
            {
                process.Close();
            }
            catch (Exception){}
           
        }
    }
}
