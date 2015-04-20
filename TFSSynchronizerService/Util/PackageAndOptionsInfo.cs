using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BuildDataDriver.Interfaces;
using NLog;

namespace FTIPusher.Util
{
  
    /// <summary>
    /// All the classes in here are related to Reading in the JSon options and acquiring the information about the core branches
    /// </summary>


    public class FtpLocation : IFtpLocation
    {
        public string FtpUrl { get; set; }
        public string FtpUser { get; set; }
        public string FtpPassWord { get; set; }
        public string FtpProxy { get; set; }
        public string FtpPort { get; set; }
        public string FTPDirectory { get; set; }
    }

    public class BuildsToWatch : IBuildsToWatch
    {
        public string Project { get; set; }
        public string Branch { get; set; }
        public int Retention { get; set; }
        public List<string> FilterInclude { get; set; }
    }

    public class ServiceOptionsRoot
    {
        public string BuildServer { get; set; }
        public string StagingFileShare { get; set; }
        public bool RunBuildUpdate { get; set; }
        public bool RunMirrorCopy { get; set; }
        public bool RunFtpUpload { get; set; }
        public bool RunHttpMirror { get; set; }
        public int UpdateFrequencyInMinutes { get; set; }
        public int MirrorLogRetention { get; set; }
        public int MirrorRetryNumber { get; set; }
        public string RESTAPIVER { get; set; }
        public List<string> Mirror { get; set; }
        public List<FtpLocation> FTPLocations { get; set; }
        public List<string> HTTPShares { get; set; }
        public List<BuildsToWatch> BuildsToWatch { get; set; }
    }

    public class ServiceOptions
    {
        public static ServiceOptionsRoot ReadJsonConfigOptions(Logger logger)
        {
            try
            {
                const string optionsFile = "FTIPusherServiceOptions.json";
                var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                logger.Info("Service Executable: {0}", loc);
                var directory = Path.GetDirectoryName(loc);
                loc = Path.Combine(directory, optionsFile);
                logger.Info("Loading options from: {0}", loc);
                if (File.Exists(loc))
                {
                    string jsonConfig;
                    using (StreamReader r = new StreamReader(loc))
                    {
                        jsonConfig = r.ReadToEnd();
                    }
                    logger.Info("Reading options file complete");
                    return new JavaScriptSerializer().Deserialize<ServiceOptionsRoot>(jsonConfig);
                }
                else
                {
                    throw new Exception("Missing configuration options file: " + optionsFile);
                }
            }
            catch (System.Exception exception)
            {
                logger.Info("Exception reading options", exception);
            }

            return null;
        }

        
    }
}
