using FTIPusher.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{
    public interface IExternalMirror
    {
        int UpdateFrequencyInMinutes { get; set; }
        List<MirrorList> MirrorList { get; set; }
    }
}
