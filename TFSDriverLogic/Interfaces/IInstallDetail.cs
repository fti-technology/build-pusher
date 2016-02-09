using System.Collections.Generic;

namespace BuildDataDriver.Interfaces
{
    public interface IInstallDetail
    {
        KeyValuePair<string, string> InstallDetails {get;set;}
        string SizeInBytes { get; set; }
        string CreationTimeUtc { get; set; }
    }
}