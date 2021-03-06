﻿using System;
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
        public string FtpId { get; set; }
        public string FtpUrl { get; set; }
        public string FtpUser { get; set; }
        public string FtpPassWord { get; set; }
        public string FtpProxy { get; set; }
        public string FtpPort { get; set; }
        public string FTPDirectory { get; set; }
        public string InternalSharePath { get; set; }
    }

    public class BuildsToWatch : IBuildsToWatch
    {
        public string Project { get; set; }
        public string Branch { get; set; }
        public int Retention { get; set; }
        public List<string> FilterInclude { get; set; }
    }

    public class FtpDestination : IFtpDestination
    {
        public string FtpId { get; set; }
        public string FTPDirectory { get; set; }
    }

    public class MirrorList : IMirrorList
    {
        public string SourceDirectory { get; set; }
        public List<string> MirrorDestinations { get; set; }
        public List<FtpDestination> FtpDestinations { get; set; }
    }

    public class ExternalMirror : IExternalMirror
    {
        public int UpdateFrequencyInMinutes { get; set; }
        public bool CreateSourceRootAtDestinations { get; set; }
        public List<MirrorList> MirrorList { get; set; }
    }

    public class ServiceOptionsRoot
    {
        public string BuildServer { get; set; }
        public string StagingFileShare { get; set; }
        public bool RunBuildUpdate { get; set; }
        public bool RunMirrorCopy { get; set; }
        public bool RunFtpUpload { get; set; }
        public bool RunHttpMirror { get; set; }
        public bool RunCheckSum { get; set; }
        public int UpdateFrequencyInMinutes { get; set; }
        public int MirrorLogRetention { get; set; }
        public int MirrorRetryNumber { get; set; }
        public string RESTAPIVER { get; set; }
        public string MONGOLOGURL { get; set; }
        public List<string> Mirror { get; set; }
        public List<FtpLocation> FTPLocations { get; set; }
        public List<string> HTTPShares { get; set; }
        public List<BuildsToWatch> BuildsToWatch { get; set; }
        public ExternalMirror ExternalMirror { get; set; }
    }

    public class ServiceOptions
    {
        
        public static ServiceOptionsRoot ReadJsonConfigOptions(ILogger logger)
        {
            return ReadJsonConfigOptions(logger, null);
        }

        public static ServiceOptionsRoot ReadJsonConfigOptions(ILogger logger, string optionsFileDirectory)
        {
            string location = null;

            try
            {
                const string optionsFile = "FTIPusherServiceOptions.json";
                if (string.IsNullOrEmpty(optionsFileDirectory))
                {
                    var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    logger.Info("Service Executable: {0}", loc);
                    var directory = Path.GetDirectoryName(loc);
                    location = Path.Combine(directory, optionsFile);
                }
                else
                {
                    location = Path.Combine(optionsFileDirectory, optionsFile);   
                }

                logger.Info("Loading options from: {0}", location);
                if (File.Exists(location))
                {
                    string jsonConfig;
                    using (StreamReader r = new StreamReader(location))
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
                logger.Info(exception, "Reading options");                
            }

            return null;
        }

        
    }
}
