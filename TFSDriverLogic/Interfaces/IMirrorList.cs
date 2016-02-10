using System.Collections.Generic;

using FTIPusher.Util;

namespace BuildDataDriver.Interfaces
{
    public interface IMirrorList
    {
        string SourceDirectory { get; set; }
        List<string> MirrorDestinations { get; set; }
        List<FtpDestination> FtpDestinations { get; set; }
    }
}