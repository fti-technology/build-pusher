using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using BuildDataDriver.Interfaces;

namespace BuildDataDriver.Data
{
  
    [DataContract]
    public class BuildPackageInformation : IBuildPackageInformation
    {
        [ScriptIgnore]
        public string BuildManifest { get; set; }

        [ScriptIgnore]
        public bool WatchBuild { get; set; }

        [DataMember]
        public string TimeZone { get; set; }

        [DataMember]
        public string Branch { get; set; }

        [DataMember]
        public string BuildServer { get; set; }

        [DataMember]
        public List<string> SourceRevs { get; set; }

        [DataMember]
        public List<Uri> BuildLinks { get; set; }

        
        // public Dictionary<string, KeyValuePair<string, string>> InstallationComponents { get; set; }
        [DataMember]
        public Dictionary<string, IInstallDetail> InstallationComponents { get; set; }

        [DataMember]
        public List<IBuildPackageDetail> BuildDetails { get; set; }

        public BuildPackageInformation()
        {
            BuildDetails = new List<IBuildPackageDetail>();
            BuildLinks = new List<Uri>();
            SourceRevs = new List<string>();
            InstallationComponents = new Dictionary<string, IInstallDetail>();
            WatchBuild = true;
        }

        public string GetDeploymentVersionNumber()
        {
            return this.BuildDetails.Max(x => x.BuildNumber).ToString(CultureInfo.InvariantCulture);
        }

        public DateTime GetBuildCompletionData()
        {
            return this.BuildDetails.Max(x => x.Completion);
        }
    }

    public class InstallDetail : IInstallDetail
    {
        public InstallDetail()
        {
            InstallDetails = new KeyValuePair<string, string>();
        }
    

        public KeyValuePair<string, string> InstallDetails
        {
            get;
            set;
        }
        public string SizeInBytes { get; set; }

        public string CreationTimeUtc { get; set; }
    }


    public class BuildPackageDetail : IBuildPackageDetail
    {
        private string _buildNumberStr;

        [DataMember]
        public string BuildDefName { get; set; }

        [DataMember]
        public Uri RemotePathTarget { get; set; }

        [DataMember]
        public string BuildName { get; set; }

        [DataMember]
        public DateTime Completion { get; set; }

        [DataMember]
        public string SourceRev { get; set; }

        [DataMember]
        public IEnumerable<string> PackageList { get; set; }

        [DataMember]
        public string BuildNumberFull
        {
            get { return _buildNumberStr; }
            set
            {
                this._buildNumberStr = value;
                // RINGTAILPROCESSINGFRAMEWORKWORKERS_AUTOMATIONYS RPF PACKAGES_20150211.1
                int index = value.LastIndexOf("_", StringComparison.InvariantCultureIgnoreCase);
                var c = value[index + 1];
                double versionNumber;

                if (double.TryParse(value.Substring(index + 1), out versionNumber))
                {
                    this.BuildNumber = versionNumber;
                }
            }
        }

        [DataMember]
        public double BuildNumber { get; protected set; }
    }
}