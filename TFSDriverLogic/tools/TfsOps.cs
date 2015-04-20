using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildDataDriver.Data;
using BuildDataDriver.Interfaces;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using NLog;
using TFSDriverLogic.Data;

namespace BuildDataDriver.tools
{
    public interface IBuildInformation
    {
        BuildPackageInformation Query(IDynamicSourceDetails dynamicSourceDetails);
        void SetDbDeployedStatus(string project, string branch, string subBranch, string version, bool deployed);
    }

    /// <summary>
    /// TFS Operations for consuming builds and finding the binaries produced by those builds in order to automatically package them
    /// </summary>
    public class TfsOps : IBuildInformation
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly Uri _tfsUri = new Uri("http://tfsserver:0000/tfs/CodeCollection");
        private TfsTeamProjectCollection _teamProjectCollection;
        private readonly DataBaseMgr _dbBaseMgr;
        // Defaults
        private const string DefTargetProject = "RootBranchNameHere";
        private const string DefTargetBranch = "DefaultBranchTarget";
        public const string DabaseLogFile = "FTIArtifactDeployer.sql3";

        private List<string> _branchList;
        
        ///////////////////////////////////////////////////////
        // Could externaize for ease of customization
        /// <summary>
        /// Artifact manifest
        /// </summary>
        internal static class BuildTarget
        {
            public static string TargetProject { get; set; }
            public static string TargetBranch { get; set; }

            // See Note
            public static List<string> TargetBuilds = new List<string> { "Ringtail8 Packages", "RPF Packages", "Classic", "DB Utility" };
            public static List<string> Manifest = new List<string>
            {
                "RingtailDatabaseUtility_{BRANCH} DB Utility_",
                "RingtailProcessingFrameworkWorkers_{BRANCH} RPF Packages_",
                "RingtailSQLComponent(x64)_v",//*_{BRANCH} SQL_
                "RingtailProcessingFramework_{BRANCH} RPF Packages_",
                "Ringtail_{BRANCH} Ringtail8 Packages_",
                "RingtailLegalAgentServer_{BRANCH} Classic_",
                "RingtailLegalHelp_{BRANCH} Classic_",
                "RingtailLegalConfigurator_{BRANCH} Classic_",
                "RingtailLegalApplicationServer_{BRANCH} Classic_"
            };
        };


        public TfsOps(string dataBaseLogDirPath)
            :this(dataBaseLogDirPath, null)
        {
        }

        public TfsOps(string dataBaseLogDirPath, string tfsPath)
        {
            if(!string.IsNullOrEmpty(tfsPath))
                this._tfsUri = new Uri(tfsPath);
            _teamProjectCollection =
                TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            BuildTarget.TargetBranch = DefTargetProject;   // defaults
            BuildTarget.TargetProject = DefTargetBranch;

            var dbLogPath = Path.Combine(dataBaseLogDirPath, DabaseLogFile);
            try
            {
                if (!Directory.Exists(dataBaseLogDirPath))
                    Directory.CreateDirectory(dataBaseLogDirPath);
            }
            catch (Exception)
            {
                // ignored
            }

            _dbBaseMgr = new DataBaseMgr(dbLogPath);
            _dbBaseMgr.Init().Wait();

             _branchList = new List<string>();
        }

        public TfsTeamProjectCollection TeamProjectCollection
        {
            get { return _teamProjectCollection; }
            set { _teamProjectCollection = value; }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="targetProject"></param>
        /// <param name="targetBranch"></param>
        /// <param name="buildsToWatch"></param>
        /// <returns></returns>
        public BuildPackageInformation PopulateBuildData(IDynamicSourceDetails dynamicSourceDetails)
        {
            BuildTarget.TargetBranch = dynamicSourceDetails.Branch;
            BuildTarget.TargetProject = dynamicSourceDetails.Project;
            return GetBuilds(dynamicSourceDetails);
        }

        public List<string> GetBranchList(List<IBuildsToWatch> buildsToWatch)
        {
            // populate a list of paths to query for based on a TfS Project and Branch
            List<string> paths = new List<string>();
            foreach (var toWatch in buildsToWatch)
            {
                var path = toWatch.Project + "/" + toWatch.Branch;
                if (!path.StartsWith("$/"))
                    path = "$/" + path;

                if (!paths.Contains(path))
                    paths.Add(path);
            }

            // Call to TFS to get the filtered list of target paths
            var baseList = BuildDataDriver.tools.TfsOps.GetBranches(this._tfsUri);
            var filteredList = from xxx in baseList
                               let itemName = xxx.Properties.RootItem.Item
                               where paths.Any(path => itemName.StartsWith(path))
                               select xxx.Properties.RootItem.Item;

            var listOfFilteredBranches = filteredList.ToList();
            for (int i = listOfFilteredBranches.Count - 1; i >= 0; i--)
            {
                // handle closure
                var targetItem = listOfFilteredBranches[i];

                foreach (var toWatch in buildsToWatch)
                {
                    if (FilterOutTfsPath(toWatch, targetItem, listOfFilteredBranches))
                    {
                        listOfFilteredBranches.RemoveAt(i);
                    }
                }

            }
            return listOfFilteredBranches;
        }

        /// <summary>
        /// Determines if the full TFS path contains a filter element or not.  The filter is used to only include paths that end with one of the FilterIncludes.
        /// Return true if the path should be filtered out.
        /// </summary>
        /// <param name="toWatch"></param>
        /// <param name="targetItem"></param>
        /// <param name="listOfFilteredBranches"></param>
        /// <returns></returns>
        private static bool FilterOutTfsPath(IBuildsToWatch toWatch, string targetItem, List<string> listOfFilteredBranches)
        {
            if (toWatch.FilterInclude != null && toWatch.FilterInclude.Count != 0)
            {
                if (targetItem.StartsWith("$/" + toWatch.Project + "/" + toWatch.Branch))
                {
                    if (toWatch.FilterInclude.FirstOrDefault(s => targetItem.EndsWith(s)) == null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public Dictionary<IDynamicSourceDetails, IBuildsToWatch> ValidateAndRefineBuildsToWatch(List<string> tfsBuildList, List<IBuildsToWatch> buildsToWatch)
        {
            
            Dictionary<IDynamicSourceDetails, IBuildsToWatch> projectBuildSpecs = new Dictionary<IDynamicSourceDetails, IBuildsToWatch>();

            foreach (var buildsToWatchItem in buildsToWatch)
            {
                // avoid closure issues
                var buildsToWatchTarget = buildsToWatchItem;
                try
                {
                    foreach (var fullBranchPath in tfsBuildList)
                    {
                        // avoid closure issues
                        var localfullBranchPath = fullBranchPath;
                        var originalPath = localfullBranchPath;
                        var subProject = localfullBranchPath.Replace("$/" + buildsToWatchTarget.Project + "/" + buildsToWatchTarget.Branch + "/", "");
                        
                        if (!string.IsNullOrEmpty(subProject) && String.CompareOrdinal(originalPath, subProject) != 0)
                        {

                            foreach (var targetBuild in BuildTarget.TargetBuilds)
                            {
                                try
                                {
                                    string subProjectExpand = subProject.Replace("/", " ");
                                    string definition = string.Format("{0} {1}", subProjectExpand, targetBuild);
                                    var buildSpec = BuildService.CreateBuildDetailSpec(buildsToWatchTarget.Project,
                                        definition);
                                    
                                    var buildDetails = BuildService.QueryBuilds(buildSpec).Builds;
                                    if (buildDetails.Any())
                                    {
                                        Console.WriteLine("Found Build Spec");
                                    }

                                    // Add project to manifest if QueryBuilds didn't throw
                                    // This consists of the original IBuildsToWatch data along with the IDynamicSourceDetails that provides the branch information
                                    DynamicSourceDetails dynamicSourceDetails = new DynamicSourceDetails(buildsToWatchTarget.Project, buildsToWatchTarget.Branch, subProject, buildsToWatchTarget.Retention);

                                    var any = projectBuildSpecs.Any(x=> (x.Key.Branch == dynamicSourceDetails.Branch) && (x.Key.Project == dynamicSourceDetails.Project) && (x.Key.SubBranch == dynamicSourceDetails.SubBranch));
                                    

                                    if (!any)
                                        projectBuildSpecs.Add(dynamicSourceDetails, buildsToWatchTarget);
                                }
                                catch (Exception ex)
                                {   
                                    // catching an exception here means the build definition didn't exist
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                    Console.WriteLine(ex);
                }
            }

             return projectBuildSpecs;
        }


        private BuildPackageInformation GetBuilds(IDynamicSourceDetails dynamicSourceDetails)
        {
            var buildService = BuildService;
            BuildPackageInformation buildPackageInformation = new BuildPackageInformation()
            {
                BuildServer = _teamProjectCollection.ConfigurationServer.Name,
                Branch = BuildTarget.TargetBranch,
                TimeZone =  _teamProjectCollection.TimeZone.ToString()
            };
            Logger.Info("TFS Build Package, Server: {0},, Branch: {1}, TimeZone: {2}", buildPackageInformation.BuildServer, buildPackageInformation.Branch, buildPackageInformation.TimeZone);
            GetBuildDetail(buildService, buildPackageInformation, dynamicSourceDetails);

            // 
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(buildPackageInformation);

            // Write that JSON to txt file
            var manifestPath = Path.Combine(Environment.CurrentDirectory + @"BuildMainifest.json");
            //File.WriteAllText(manifestPath, json); //TODO: FIX ME for ASYNC
            buildPackageInformation.BuildManifest = manifestPath;
            Logger.Info("Build Manifest Path set: {0}", buildPackageInformation.BuildManifest);

            return buildPackageInformation;
        }

        private void GetBuildDetail(IBuildServer buildService, BuildPackageInformation buildPackageInformation, IDynamicSourceDetails dynamicSourceDetails)
        {
            foreach (var targetBuild in BuildTarget.TargetBuilds)
            {
                IEnumerable<string> buildDropPaths = new List<string>();
                IBuildDetail myBuildDetail = null;
                string sourceGetVersion = string.Empty;
                IBuildDefinition buildDefinition = null;

                string subProjectExpand = dynamicSourceDetails.SubBranch.Replace("/", " ");
                string definition = string.Format("{0} {1}", subProjectExpand, targetBuild);
                try
                {
                    //var definition = string.Format("{0} {1}", dynamicSourceDetails.SubBranch, targetBuild);
                    buildDefinition = buildService.GetBuildDefinition(BuildTarget.TargetProject, definition);
                        //BuildTarget.TargetBranch + " " + targetBuild);
                }
                catch (Exception ex)
                {
                    Logger.InfoException("Exception in GetBuildDetail", ex);
                    // TODO; LOG MAJOR ERROR

                }

                if (buildDefinition == null || buildDefinition.LastGoodBuildUri == null)
                {
                    Logger.Error("Error, buildDefinition or LastGoodBuildUri not found for {0} - {1} - {2}", BuildTarget.TargetProject, BuildTarget.TargetBranch, targetBuild);
                    continue;   // continue or remove this build entirely?                    
                }

                Uri lastKnownGoodBuild = buildDefinition.LastGoodBuildUri;

                myBuildDetail = buildService.GetBuild(lastKnownGoodBuild);
                string dropLocation = myBuildDetail.DropLocation;
                Logger.Info("Drop location found: {0}", dropLocation);
                sourceGetVersion = myBuildDetail.SourceGetVersion; // C223345
                
                // TARGET/BRANCH/VERSION
                if (!buildPackageInformation.SourceRevs.Contains(sourceGetVersion))
                    buildPackageInformation.SourceRevs.Add(sourceGetVersion);
                if (!buildPackageInformation.SourceRevs.Contains(sourceGetVersion))
                    buildPackageInformation.SourceRevs.Add(sourceGetVersion);

                if (!buildPackageInformation.BuildLinks.Contains(buildDefinition.LastGoodBuildUri))
                    buildPackageInformation.BuildLinks.Add(buildDefinition.LastGoodBuildUri);

                buildDropPaths = BuildUpDropPaths(dropLocation, dynamicSourceDetails.SubBranch,
                    buildPackageInformation.InstallationComponents, myBuildDetail.BuildNumber);

                BuildPackageDetail buildPackageDetail = new BuildPackageDetail()
                {
                    BuildDefName = buildDefinition.Name,
                    RemotePathTarget = myBuildDetail.Uri,
                    BuildName = myBuildDetail.BuildNumber,
                    Completion = myBuildDetail.FinishTime,
                    SourceRev = sourceGetVersion,
                    BuildNumberFull = myBuildDetail.BuildNumber,
                    PackageList = buildDropPaths
                };
                buildPackageInformation.BuildDetails.Add(buildPackageDetail);
            }
        }

        private IBuildServer BuildService
        {
            get
            {
                if (_teamProjectCollection == null) return null;

                var buildService = (IBuildServer) _teamProjectCollection.GetService(typeof (IBuildServer));
                return buildService;
            }
        }

        internal IEnumerable<string> BuildUpDropPaths(string dropLoc, string branch, Dictionary<string, KeyValuePair<string,string>> setupPackageList, string buildNumber)
        {
            List<string> componentsRawList = new List<string>();
            var temp = Directory.EnumerateFiles(dropLoc, "*.exe", SearchOption.AllDirectories);
            List<string>manifestBranchList = BuildTarget.Manifest.Select(manifest => manifest.Replace("{BRANCH}", branch)).ToList();

            foreach (var file in temp)
            {
                var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                foreach (var manifestItem in manifestBranchList.Where(manifestItem => fileName.ToUpperInvariant().StartsWith(manifestItem.ToUpperInvariant())).Where(manifestItem => !setupPackageList.ContainsKey(fileName)))
                {
                    setupPackageList.Add(fileName, new KeyValuePair<string, string>(buildNumber, file));
                    componentsRawList.Add(file);
                    Logger.Info("Drop path added: {0} - for Branch: {1}, Build Number: {2}", file, branch, buildNumber);
                }
            }

            return componentsRawList;
        }

        // QUERY TFS for all Root branch objects that haven't been deleted 

        private static IEnumerable<BranchObject> GetBranches(Uri buildServerPath)
        {
            var tfs = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(buildServerPath);
            var versionControlServer = tfs.GetService<VersionControlServer>();


            var baseList = versionControlServer.QueryRootBranchObjects(RecursionType.Full)
                .Where(b => !b.Properties.RootItem.IsDeleted);
            return baseList;
        }    

        public BuildPackageInformation Query(IDynamicSourceDetails dynamicSourceDetails)
        {
            var buildData = PopulateBuildData(dynamicSourceDetails);

            // Query DB

            //var query = _dbBaseMgr.QueryWithChild(project, branch, buildData.GetDeploymentVersionNumber());
            var query = _dbBaseMgr.QueryWithChild(dynamicSourceDetails.Project, dynamicSourceDetails.SubBranch, buildData.GetDeploymentVersionNumber());
            query.Wait();
            if (query.Result != null)
            {
                buildData.WatchBuild = false;
                return buildData;                
            }

            // Build Up list of package details for the database... Many of these correspond back to one PackageArtifactData
            List<ArtifactDetail> artifactDetails = (from buildDetail in buildData.BuildDetails
                from package in buildDetail.PackageList
                select new ArtifactDetail()
                {
                    BuildNumber = buildDetail.BuildNumberFull, PackageFullPath = package,PackageName = Path.GetFileName(package), PackageCompletionTime = buildDetail.Completion
                }).ToList();

            // Master DB build detail
            PackageArtifactData packageArtifactData = new PackageArtifactData()
            {
                Branch = buildData.Branch,
                Project = dynamicSourceDetails.Project,
                SubProject = dynamicSourceDetails.SubBranch,
                Version = buildData.GetDeploymentVersionNumber(),
                Deployed = false,
                RecordTime = DateTime.Now,
                DeployedDate = DateTime.Now,
                PackageDetails = artifactDetails,
                BuildCompletion = buildData.GetBuildCompletionData()
            };

            _dbBaseMgr.InsertRecordWithChildren(packageArtifactData).Wait();

            return buildData;
        }



        public void SetDbDeployedStatus(string project, string branch,string subProject, string version, bool deployed)
        {
            var ret = _dbBaseMgr.QueryByProjBranchVer(project, branch, subProject, version);
            ret.Wait();


            if (ret.Result != null)
            {
                var artifactData = ret.Result;

                artifactData.Deployed = deployed;
                var updateRet = _dbBaseMgr.Update(artifactData);
                updateRet.Wait();
            }
        }

        public void UpdateCheckSum(string project, string branch, string subProject, string version, string path)
        {
            try
            {

                var ret = _dbBaseMgr.QueryByProjBranchVer(project, branch, subProject, version);
                ret.Wait();


                string key = Path.GetFileName(path);
                if (ret.Result != null)
                {
              
                    var artifactData = ret.Result;
                    ArtifactDetail detail = null;

                    if (artifactData == null || artifactData.PackageDetails == null)
                        return;

                    foreach (
                       var item in
                           artifactData.PackageDetails.Where(
                               x => (String.Compare(x.PackageName, key, StringComparison.CurrentCultureIgnoreCase) == 0)))
                    {
                        detail = item;
                        break;
                    }

                    if (detail != null)
                    {
                        detail.Md5 = FileUtils.ComputeMd5(path);
                        var updateWithChildren = _dbBaseMgr.UpdateWithChildren(detail);
                        updateWithChildren.Wait();
                    }

                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

    }

}
