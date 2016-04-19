using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BuildDataDriver.Interfaces;
using BuildDataDriver.tools;

using FTIPusher.Util;

namespace TestDriver
{
    class TFSDriver
    {
        /// <summary>
        /// USED FOR TESTING
        /// </summary>
        /// <param name="TFSFullProjectPath"></param>
        /// <param name="tfsServer"></param>
        public static void DriveGetLastBuildDetails(string TFSFullProjectPath, string tfsServer)
        {
            var buildToWach = new List<IBuildsToWatch>()
                                  {
                                      new BuildsToWatch
                                          {
                                              Project = "Ringtail",
                                              Branch = "Main",
                                              Retention = 5
                                          }
                                  };

            TfsOps _tfsOps = new TfsOps(System.IO.Path.GetTempPath(), tfsServer);

            var branchList = _tfsOps.GetBranchList(buildToWach);

            var validateAndRefineBuildsToWatch = _tfsOps.ValidateAndRefineBuildsToWatch(branchList,buildToWach);
        }
    
    }
}
