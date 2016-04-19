using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildDataDriver.Data;
using BuildDataDriver.Interfaces;
using BuildDataDriver.Util;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json;
using NLog;
using TFSDriverLogic.Data;
using BuildDataDriver.Extensions;

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
            public static List<string> TargetBuilds = new List<string> { "Ringtail8 Packages", "RPF Packages", "Main SQL Component", "DB Utility" };
            public static List<string> Manifest = new List<string>
            {
                "RingtailDatabaseUtility_{BRANCH} DB Utility_",
                "RingtailProcessingFrameworkWorkers_{BRANCH} RPF Packages_",
                "RingtailSQLComponent(x64)_v_{BRANCH} SQL_Component_",
                "RingtailProcessingFramework_{BRANCH} RPF Packages_",
                "Ringtail_{BRANCH} Ringtail8 Packages_"
            };
        };


        public TfsOps(string dataBaseLogDirPath)
            : this(dataBaseLogDirPath, null)
        {
        }

        public TfsOps(string dataBaseLogDirPath, string tfsPath)
        {
            if (!string.IsNullOrEmpty(tfsPath))
                this._tfsUri = new Uri(tfsPath);

            try
            {
                Logger.Info("TFS Server Set: " + _tfsUri);
                _teamProjectCollection =
                TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            }
            catch (Exception ex)
            {
                Logger.FatalException("TFS Server connection, failed, please check TFS path in config", ex);
            }

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

        public static void GetSomeBD(Uri _tfsUri)
        {
            var tfsTeamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            var buildServer = (IBuildServer)tfsTeamProjectCollection.GetService(typeof(IBuildServer));
        }


        /// <summary>
        /// Used to query for the last success build on a TFS project/branch.  Try to initially go back a week, but still grab last successful.
        /// </summary>
        /// <param name="TFSFullProjectPath"></param>
        /// <param name="_tfsUri"></param>
        /// <returns></returns>
        public static List<IBuildDetail> GetLastBuildDetails(string TFSFullProjectPath, Uri _tfsUri)
        {
            List<IBuildDetail> buildDetails = new List<IBuildDetail>(); ;
            var tfsTeamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            var buildServer = (IBuildServer)tfsTeamProjectCollection.GetService(typeof(IBuildServer));
            var dollarIndex = TFSFullProjectPath.IndexOf("$/", StringComparison.Ordinal);
            if (dollarIndex != -1)
            {

                var subProject = TFSFullProjectPath.Substring(TFSFullProjectPath.LastIndexOf('/')).Replace("/", "");
                string tmpStr = TFSFullProjectPath.Replace("$/", "");
                string project = tmpStr.Substring(0, tmpStr.IndexOf("/", StringComparison.Ordinal));
                //TODO - fix for projects that don't map from TFS source to build

                foreach (var targetBuild in BuildTarget.TargetBuilds)
                {
                    try
                    {
                        string definition = string.Format("{0} {1}", subProject, targetBuild);
                        var buildSpec = buildServer.CreateBuildDetailSpec(project, definition);
                        buildSpec.MaxBuildsPerDefinition = 1;
                        buildSpec.QueryOrder = BuildQueryOrder.FinishTimeDescending;
                        buildSpec.InformationTypes = null;
                        buildSpec.QueryDeletedOption = QueryDeletedOption.ExcludeDeleted;
                        buildSpec.Status = BuildStatus.Succeeded;
                        buildSpec.MinFinishTime = DateTime.Now.AddDays(-7);
                        buildSpec.MaxFinishTime = DateTime.Now;
                        var buildRecord = GetBuildRecord(buildServer, buildSpec);

                        if (buildRecord != null)
                        {
                            buildDetails.Add(buildRecord);
                        }
                        else
                        {
                            // Try to increase the query
                            buildSpec.MinFinishTime = DateTime.Now.AddDays(-60);
                            buildRecord = GetBuildRecord(buildServer, buildSpec);
                            if (buildRecord != null)
                            {
                                buildDetails.Add(buildRecord);
                            }
                        }

                        //                        DownLoadFromDropPath(buildRecord, subProject);
                    }
                    catch (Exception exception)
                    {
                    }
                }

            }

            return buildDetails;
        }

        public static Task<List<string>> DownLoadPathsFromDropPathAsync(IEnumerable<IBuildDetail> buildRecords, string branch)
        {
            return Task.Run(() =>
            {
                List<string> dropPaths = new List<string>();
                if (buildRecords != null)
                {
                    var intallationComponents = new Dictionary<string, IInstallDetail>();
                    foreach (var buildRecord in buildRecords)
                    {
                        dropPaths.AddRange(BuildUpDropPaths(buildRecord.DropLocation, branch,
                            ref intallationComponents, buildRecord.BuildNumber, true));
                    }
                }

                return dropPaths;
            });
        }

        public static IEnumerable<string> DownLoadPathsFromDropPath(IEnumerable<IBuildDetail> buildRecords, string branch)
        {

            List<string> dropPaths = new List<string>();
            if (buildRecords != null)
            {
                var intallationComponents = new Dictionary<string, IInstallDetail>();
                foreach (var buildRecord in buildRecords)
                {
                    dropPaths.AddRange(BuildUpDropPaths(buildRecord.DropLocation, branch,
                        ref intallationComponents, buildRecord.BuildNumber, true));
                }
            }

            return dropPaths;
        }

        private static IBuildDetail GetBuildRecord(IBuildServer buildServer, IBuildDetailSpec buildSpec)
        {
            var buildRecord = buildServer.QueryBuilds(buildSpec).Builds.FirstOrDefault();
            return buildRecord;
        }

        //Static override
        public static List<string> GetBranchList(List<IBuildsToWatch> buildsToWatch, Uri tfsPath)
        {
            return ListOfFilteredBranches(buildsToWatch, tfsPath);
        }

        public List<string> GetBranchList(List<IBuildsToWatch> buildsToWatch)
        {
            return ListOfFilteredBranches(buildsToWatch, this._tfsUri);
        }

        /// <summary>
        /// Pass in top level project name, Full TFS source path, and TFS server Uri and this function
        /// will return a list of all build definitions that match the criteria.  This is good for when 
        /// the TFS source name, doesn't match the Build definition.
        /// </summary>
        /// <param name="TFSProject"></param>
        /// <param name="TFSSourcePath"></param>
        /// <param name="_tfsUri"></param>
        /// <returns></returns>
        private List<IBuildDefinition> FindBuildDefintions(string TFSProject, string TFSSourcePath, Uri _tfsUri)
        {
            List<IBuildDefinition> list = new List<IBuildDefinition>();
            var tfsTeamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            var buildServer = (IBuildServer)tfsTeamProjectCollection.GetService(typeof(IBuildServer));

            //Project Ringtail 

            foreach (var buildDefinition in buildServer.QueryBuildDefinitions(TFSProject, QueryOptions.Definitions | QueryOptions.Workspaces))
            {

                if (buildDefinition.Workspace.Mappings.Any(workspaceMapping => workspaceMapping.ServerItem.Contains(TFSSourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!list.Contains(buildDefinition))
                        list.Add(buildDefinition);
                }

            }
            return list;
        }
        /// <summary>
        /// Pass in top level project name, Full TFS source path, and TFS server Uri and this function
        /// will return a list of all build definitions that match the criteria.  This is good for when 
        /// the TFS source name, doesn't match the Build definition.
        /// </summary>
        /// <param name="TFSProject"></param>
        /// <param name="TFSSourcePath"></param>
        /// <param name="_tfsUri"></param>
        /// <returns></returns>
        private IBuildDefinition FindBuildDefintion(string TFSProject, string TFSSourcePath, Uri _tfsUri)
        {
            var tfsTeamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(_tfsUri);
            var buildServer = (IBuildServer)tfsTeamProjectCollection.GetService(typeof(IBuildServer));

            //Project Ringtail 

            foreach (var buildDefinition in buildServer.QueryBuildDefinitions(TFSProject, QueryOptions.Definitions | QueryOptions.Workspaces))
            {

                if (buildDefinition.Workspace.Mappings.Any(workspaceMapping => workspaceMapping.ServerItem.Contains(TFSSourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    return buildDefinition;
                }

            }
            return null;
        }

        /// <summary>
        /// Internal use
        /// </summary>
        /// <param name="buildsToWatch"></param>
        /// <param name="tfsPath"></param>
        /// <returns></returns>
        private static List<string> ListOfFilteredBranches(List<IBuildsToWatch> buildsToWatch, Uri tfsPath)
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
            var baseList = BuildDataDriver.tools.TfsOps.GetBranches(tfsPath);

#if DEBUG
            Logger.Debug("Branches Found: " + baseList.Count());
            Logger.Debug("---------------------------------------------------------");
            foreach (var branchObject in baseList)
            {
                Logger.Debug(branchObject.Properties.RootItem.Item);
            }
            Logger.Debug("---------------------------------------------------------");
#endif
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
                        // Need unit test here
                        if (!string.IsNullOrEmpty(subProject) )//&& String.CompareOrdinal(originalPath, subProject) != 0)
                        {

                            foreach (var targetBuild in BuildTarget.TargetBuilds)
                            {
                                try
                                {
                                    string subProjectExpand = string.Empty;
                                    if (String.CompareOrdinal(originalPath, subProject) != 0)
                                    {
                                        
                                            subProjectExpand = subProject.Replace("/", " ");
                                    }
                                    else if (!originalPath.Contains(buildsToWatchTarget.Branch))
                                    {
                                        subProjectExpand = subProject.Replace("/", " ");
                                    }

                                    if(string.IsNullOrEmpty(subProjectExpand))
                                    {
                                        subProjectExpand = buildsToWatchTarget.Branch;
                                    }

                                    string definition = string.Format("{0} {1}", subProjectExpand, targetBuild);
                                    try
                                    {
                                        var buildSpec = BuildService.CreateBuildDetailSpec(buildsToWatchTarget.Project,
                                        definition);

                                        var buildDetails = BuildService.QueryBuilds(buildSpec).Builds;
                                        if (buildDetails.Any())
                                        {
                                            Logger.Info("Found Build Spec: " + buildSpec.DefinitionSpec.Name);
                                        }
                                        else
                                        {
                                            // FUTURE OPTIMIZATION, make this call once per branch and cache results.  For now, only call when necessary
                                            var buildDefinition = FindBuildDefintion(buildsToWatchTarget.Project, fullBranchPath, _tfsUri);
                                            if (buildDefinition != null)
                                            {
                                                buildSpec = BuildService.CreateBuildDetailSpec(buildDefinition);
                                                buildDetails = BuildService.QueryBuilds(buildSpec).Builds;
                                                if (buildDetails.Any())
                                                {
                                                    Logger.Info("Found Build Spec: " + buildSpec.DefinitionSpec.Name);
                                                }
                                            }
                                        }

                                        if (!buildDetails.Any())
                                        {
                                            Logger.Info("No Details found for Build Spec: " +
                                                           buildSpec.DefinitionSpec.Name);
                                        }

                                        if (buildSpec == null)
                                        {
                                            throw new Exception("Build SPEC NOT FOUND: " + definition);
                                        }

                                        // Add project to manifest if QueryBuilds didn't throw
                                        // This consists of the original IBuildsToWatch data along with the IDynamicSourceDetails that provides the branch information
                                        DynamicSourceDetails dynamicSourceDetails = new DynamicSourceDetails(buildsToWatchTarget.Project, buildsToWatchTarget.Branch, subProject, buildsToWatchTarget.Retention, buildSpec);

                                        var any = projectBuildSpecs.Any(x => (x.Key.Branch == dynamicSourceDetails.Branch) && (x.Key.Project == dynamicSourceDetails.Project) && (x.Key.SubBranch == dynamicSourceDetails.SubBranch));


                                        if (!any)
                                            projectBuildSpecs.Add(dynamicSourceDetails, buildsToWatchTarget);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.InfoException("BUILD SPEC NOT FOUND EXCEPTION HANDLED", ex);
                                    }
                                    
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
                TimeZone = _teamProjectCollection.TimeZone.ToString()
            };
            Logger.Info("TFS Build Package, Server: {0},, Branch: {1}, TimeZone: {2}", buildPackageInformation.BuildServer, buildPackageInformation.Branch, buildPackageInformation.TimeZone);
            GetBuildDetail(buildService, buildPackageInformation, dynamicSourceDetails);

            // 
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(buildPackageInformation);
            buildPackageInformation.BuildManifest = json;
            Logger.Info("Build Manifest Path set: {0}", buildPackageInformation.BuildManifest);

            return buildPackageInformation;
        }

        private void GetBuildDetail(IBuildServer buildService, BuildPackageInformation buildPackageInformation,
            IDynamicSourceDetails dynamicSourceDetails)
        {
            ConcurrentDictionary<IBuildDefinition, IBuildDetail> concurrentDictionary = new ConcurrentDictionary<IBuildDefinition, IBuildDetail>();
            foreach (var targetBuild in BuildTarget.TargetBuilds)
            {
                // NEED UNIT TESTING!
                // Parallel.ForEach(BuildTarget.TargetBuilds, targetBuild =>
                // {
                IBuildDefinition buildDefinition = null;
                var subProjectExpand = String.CompareOrdinal(dynamicSourceDetails.SubBranch,
                                   "$/" + dynamicSourceDetails.Project + "/" + dynamicSourceDetails.Branch) == 0 ? dynamicSourceDetails.Branch : dynamicSourceDetails.SubBranch.Replace("/", " ");

                string definition = string.Format("{0} {1}", subProjectExpand, targetBuild);
                try
                {
                    //var definition = string.Format("{0} {1}", dynamicSourceDetails.SubBranch, targetBuild);
                    buildDefinition = buildService.GetBuildDefinition(BuildTarget.TargetProject, definition);
                    //BuildTarget.TargetBranch + " " + targetBuild);

                    Uri lastKnownGoodBuild = buildDefinition.LastGoodBuildUri;

                    IBuildDetail myBuildDetail = buildService.GetBuild(lastKnownGoodBuild);
                    concurrentDictionary.TryAdd(buildDefinition, myBuildDetail);
                }
                catch (Exception ex)
                {
                    Logger.InfoException("Exception in GetBuildDetail HANDLED", ex);
                    // TODO; LOG MAJOR ERROR
                }

                if (buildDefinition == null || buildDefinition.LastGoodBuildUri == null)
                {
                    Logger.Error("Error, buildDefinition or LastGoodBuildUri not found for {0} - {1} - {2}",
                        BuildTarget.TargetProject, BuildTarget.TargetBranch, targetBuild);
                }

            }
            //});

           // foreach (var buildDefinition in concurrentBag)
            foreach (var buildTargetItem in concurrentDictionary)
            {
             
                IEnumerable<string> buildDropPaths = new List<string>();
                string sourceGetVersion = string.Empty;
                //IBuildDetail myBuildDetail = null;
                //Uri lastKnownGoodBuild = buildDefinition.LastGoodBuildUri;

                //myBuildDetail = buildService.GetBuild(lastKnownGoodBuild);
                string dropLocation = buildTargetItem.Value.DropLocation;
                Logger.Info("Drop location found: {0}", dropLocation);
                sourceGetVersion = buildTargetItem.Value.SourceGetVersion; // C223345

                // TARGET/BRANCH/VERSION
                if (!buildPackageInformation.SourceRevs.Contains(sourceGetVersion))
                    buildPackageInformation.SourceRevs.Add(sourceGetVersion);
                if (!buildPackageInformation.SourceRevs.Contains(sourceGetVersion))
                    buildPackageInformation.SourceRevs.Add(sourceGetVersion);

                if (!buildPackageInformation.BuildLinks.Contains(buildTargetItem.Key.LastGoodBuildUri))
                    buildPackageInformation.BuildLinks.Add(buildTargetItem.Key.LastGoodBuildUri);

                var tempVar = buildPackageInformation.InstallationComponents;

                var branchTarget = dynamicSourceDetails.SubBranch.Contains("$")
                    ? dynamicSourceDetails.Branch
                    : dynamicSourceDetails.SubBranch;
                buildDropPaths = BuildUpDropPaths(dropLocation, branchTarget,
                    ref tempVar, buildTargetItem.Value.BuildNumber);
                buildPackageInformation.InstallationComponents = tempVar;

                BuildPackageDetail buildPackageDetail = new BuildPackageDetail()
                {
                    BuildDefName = buildTargetItem.Key.Name,
                    RemotePathTarget = buildTargetItem.Value.Uri,
                    BuildName = buildTargetItem.Value.BuildNumber,
                    Completion = buildTargetItem.Value.FinishTime,
                    SourceRev = sourceGetVersion,
                    BuildNumberFull = buildTargetItem.Value.BuildNumber,
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

                var buildService = (IBuildServer)_teamProjectCollection.GetService(typeof(IBuildServer));
                return buildService;
            }
        }

        internal IEnumerable<string> BuildUpDropPaths(string dropLoc, string branch,
            ref Dictionary<string, IInstallDetail> setupPackageList, string buildNumber)
        {
            return BuildUpDropPaths(dropLoc, branch, ref setupPackageList, buildNumber, true);
        }

        private static IEnumerable<string> BuildUpDropPaths(string dropLoc, string branch, ref Dictionary<string, IInstallDetail>  setupPackageList, string buildNumber, bool internalCall)
        {
            //ref Dictionary<string, IInstallDetail> setupPackageList
            //Dictionary<string, KeyValuePair<string, string>> setupPackageListTemp = new Dictionary<string, KeyValuePair<string, string>>();
            var setupPackageListTemp = new Dictionary<string, IInstallDetail>();
            List<string> componentsRawList = new List<string>();
            IEnumerable<string> filePaths;
            if (setupPackageList.Any())
                setupPackageListTemp = setupPackageList.DeepCopy();
            try
            {
                filePaths = Directory.EnumerateFiles(dropLoc, "*.exe", SearchOption.AllDirectories);
                Logger.Info("Found {0} files at drop loc: {1}", filePaths.Count(),dropLoc);
                foreach (var filePath in filePaths)
                {
                    Logger.Info("File: {0}", filePath);
                }
            }
            catch (Exception)
            {

                return componentsRawList;
            }

            List<string> manifestBranchList = BuildTarget.Manifest.Select(manifest => manifest.Replace("{BRANCH}", branch)).ToList();
            object sync = new object();
            //foreach (var file in filePaths)
            Parallel.ForEach(filePaths, file =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                foreach (var manifestItem in manifestBranchList.Where(manifestItem => fileName.ToUpperInvariant().StartsWith(manifestItem.ToUpperInvariant())).Where(manifestItem => !setupPackageListTemp.ContainsKey(fileName)))
                {
                    long fileSize = 0;
                    DateTime creationDateTime;
                    try
                    {
                        FileInfo f = new FileInfo(file);
                        fileSize = f.Length;
                    }
                    catch (Exception){}
                    
                    lock (sync)
                    {
                        setupPackageListTemp.Add(fileName, new InstallDetail(){ InstallDetails = new KeyValuePair<string, string>(buildNumber, file), SizeInBytes = fileSize.ToString()});  //new KeyValuePair<string, string>(buildNumber, file));
                        componentsRawList.Add(file);
                    }
                    Logger.Info("Drop path added: {0} - for Branch: {1}, Build Number: {2}", file, branch, buildNumber);
                }
            });

            if (setupPackageListTemp.Any())
                setupPackageList = setupPackageListTemp.DeepCopy();

            return componentsRawList;
        }

        // QUERY TFS for all Root branch objects that haven't been deleted 

        private static IEnumerable<BranchObject> GetBranches(Uri buildServerPath)
        {
            VersionControlServer versionControlServer = null;
            try
            {
                var tfs = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(buildServerPath);
                versionControlServer = tfs.GetService<VersionControlServer>();

            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error connecting to VersionControlServer", ex);

            }

            if (versionControlServer == null)
                return new List<BranchObject>();

            return versionControlServer.QueryRootBranchObjects(RecursionType.Full).Where(b => !b.Properties.RootItem.IsDeleted);
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
                                                        BuildNumber = buildDetail.BuildNumberFull,
                                                        PackageFullPath = package,
                                                        PackageName = Path.GetFileName(package),
                                                        PackageCompletionTime = buildDetail.Completion
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



        public void SetDbDeployedStatus(string project, string branch, string subProject, string version, bool deployed)
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
