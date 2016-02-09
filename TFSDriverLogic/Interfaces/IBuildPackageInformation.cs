using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BuildDataDriver.Data;

namespace BuildDataDriver.Interfaces
{
    public interface IBuildPackageInformation
    {        
        string BuildManifest { get; set; }
        string TimeZone { get; set; }
        string Branch { get; set; }
        string BuildServer { get; set; }
        List<string> SourceRevs { get; set; }
        List<Uri> BuildLinks { get; set; }
        //Dictionary<string, KeyValuePair<string, string>> InstallationComponents { get; set; }
        Dictionary<string, IInstallDetail> InstallationComponents { get; set; }
        List<IBuildPackageDetail> BuildDetails { get; set; }
        string GetDeploymentVersionNumber();
        DateTime GetBuildCompletionData();
        
    }
}
