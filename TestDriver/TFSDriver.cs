using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BuildDataDriver.Data;
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



            foreach (var dynamicSourceDetails in validateAndRefineBuildsToWatch)
            {
                BuildPackageInformation buildInformation = null;
                try
                {
                    buildInformation = _tfsOps.Query(dynamicSourceDetails.Key);
                }
                catch (Exception ex)
                {
                    //_logger.ErrorException("Exception querrying build system!", ex);
                }

                if ((buildInformation != null) && (buildInformation.WatchBuild))
                {
                    Console.WriteLine("Adding build to watch to Queue: {0} - {1}, VERSION: {2}",
                        dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch,
                        buildInformation.GetDeploymentVersionNumber());
                  
                }
                else
                {
                    Console.WriteLine("Skipping addition of build to the queue because Query failed: {0} - {1}",
                        dynamicSourceDetails.Value.Project, dynamicSourceDetails.Value.Branch);
                }
            }
        }
    
    }
}
