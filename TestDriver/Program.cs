using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDriver
{
    class Program
    {
        static void Main(string[] args)
        {
            string tfsServerPath = "http://[TFSSERVERHERE]/tfs/[CollectionHERE]";

            TFSDriver.DriveGetLastBuildDetails("Main SQL Component", tfsServerPath);

        }
    }
}
