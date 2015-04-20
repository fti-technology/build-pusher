using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildDataDriver.Interfaces
{
    public interface IDynamicSourceDetails
    {
        string Project { get; }
        string Branch { get; }
        string SubBranch { get; }
        int Retention { get; }
    }
}
