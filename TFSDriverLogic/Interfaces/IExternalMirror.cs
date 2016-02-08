using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{
    public interface IExternalMirror
    {
        string SourceDirectory { get; set; }
        int UpdateFrequencyInMinutes { get; set; }
        List<string> MirrorDestinations { get; set; }
    }
}
