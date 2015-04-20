using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{
    public interface IBuildsToWatch
    {
        string Project { get; set; }
        string Branch { get; set; }
        int Retention { get; set; }
        List<string> FilterInclude { get; set; }
    }
}
