using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{    
    public interface IBuildPackageDetail
    {
        string BuildDefName { get; set; }
        Uri RemotePathTarget { get; set; }
        string BuildName { get; set; }
        DateTime Completion { get; set; }
        string SourceRev { get; set; }
        IEnumerable<string> PackageList { get; set; }
        string BuildNumberFull { get; set; }
        double BuildNumber { get; }
    }
    
}
