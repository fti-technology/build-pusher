using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildDataDriver.Interfaces;
using Microsoft.TeamFoundation.Build.Client;

namespace BuildDataDriver.Data
{
   
    /// <summary>
    /// Helper class to hold the data for the dynamic branch queries.
    /// So for instance, we query on TFS project "projectA" and Branch "branchA".  There 
    /// might also be a filter to grab branches under "branchA".  In the TFS world, folders aren't branches,
    /// but seem to appear that way.  So there are cases where we need the first part and the last parts of the path
    /// Example:
    /// $/projectA/Releases/v1.0/Hotfix
    /// In the case and based on the json options for specifying this: Project: "projectA", Branch: "Releases", Filter: "Hotfix"
    /// In reality the project is considered a project in TFS, branch is just a folder along with v1.0.  Hotfix is actually a branch.
    /// TFS needs the folder paths to fully qualify a path.
    /// </summary>
    public class DynamicSourceDetails : IDynamicSourceDetails, IComparer<DynamicSourceDetails>, IEqualityComparer<IDynamicSourceDetails>
    {
        public string Project { get; internal set; }
        public string Branch { get; internal set; }
        public string SubBranch { get; internal set; }
        public int Retention { get; internal set; }
        public IBuildDetailSpec BuildSpec { get; set; }


        public DynamicSourceDetails(string project, string branch, string subBranch, int numberOfBuildsToRetain, IBuildDetailSpec buildSpec)
        {
            Project = project;
            Branch = branch;
            SubBranch = subBranch;
            Retention = numberOfBuildsToRetain;
            BuildSpec = buildSpec;
        }

          public bool Equals(IDynamicSourceDetails x, IDynamicSourceDetails y)
        {
            return x.Branch == y.Branch && x.Project == y.Project && x.Retention == y.Retention &&
                   x.SubBranch == y.SubBranch;
        }
          public int GetHashCode(IDynamicSourceDetails obj)
          {
              return obj.GetHashCode() + obj.GetHashCode() + obj.GetHashCode() + obj.GetHashCode();
          }
          public bool Equals(DynamicSourceDetails other)
          {
              return Branch == other.Branch && Project == other.Project && Retention == other.Retention &&
                         SubBranch == other.SubBranch;
          }
        public int Compare(DynamicSourceDetails x, DynamicSourceDetails y)
        {  
            if( x.Branch == y.Branch && x.Project == y.Project && x.Retention == y.Retention && x.SubBranch == y.SubBranch)
                return 1;
            return 0;
        }
    }
}
