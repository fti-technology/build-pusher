using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using BuildDataDriver.Interfaces;

namespace BuildDataDriver.tools
{

    public class ArtifactoryDriver
    {
        private static readonly string RemoteUri = "http://someserver/artifactory/";
        private const string User = "admin";    // defaults
        private const string Pw = "admin";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artifactoryRepoRoot"></param>
        /// <param name="product"></param>
        /// <param name="branch"></param>
        /// <param name="buildInformation"></param>
        /// <returns></returns>
        private static string GetArtifactoryUri(string artifactoryRepoRoot, string product, string branch,
            IBuildPackageInformation buildInformation)
        {

            //dest/branch/version
            var version = buildInformation.GetDeploymentVersionNumber();
            string[] paths = {RemoteUri, artifactoryRepoRoot, product, branch, version};

            Uri baseUri = new Uri(RemoteUri);
            Uri myUri = new Uri(baseUri, artifactoryRepoRoot + "/" + product + "/" + branch + "/" + version + "/");

            return myUri.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artifactoryRepoRoot"></param>
        /// <param name="product"></param>
        /// <param name="branch"></param>
        /// <param name="buildInformation"></param>
        /// <param name="localArtifactRootDir"></param>
        /// <param name="properties"></param>
        public static void Upload(string artifactoryRepoRoot, string product, string branch,
            IBuildPackageInformation buildInformation, string localArtifactRootDir, Dictionary<string, string> properties = null)
        {

            string uri = GetArtifactoryUri(artifactoryRepoRoot, product, branch, buildInformation);


            string propertyDecoratedUri = ";branch=" + buildInformation.Branch + ";sourcerevs=" +
                                             buildInformation.SourceRevs.Aggregate((x, y) => x + "," + y) + ";version=" + buildInformation.GetDeploymentVersionNumber();
            using (var client = new System.Net.WebClient())
            {
                client.UseDefaultCredentials = true;
                client.Credentials = new NetworkCredential(User, Pw);
                client.UploadData(Uri.EscapeUriString(uri) + propertyDecoratedUri, "PUT", new byte[] { });
            }

            foreach (var installationComponentKvp in buildInformation.InstallationComponents)
            {

                var fileName = Path.GetFileName(installationComponentKvp.Value.Value);
                var remotePath = Uri.EscapeUriString(uri + fileName) + propertyDecoratedUri;
                RunCurlUpload(User, Pw, Path.Combine(localArtifactRootDir, fileName), remotePath);
                
            }

            var manifestFile = Path.GetFileName(buildInformation.BuildManifest);
            var manifestPath = Path.Combine(localArtifactRootDir, manifestFile);
            if (File.Exists(manifestPath))
            {
                var remotePath = Uri.EscapeUriString(uri + manifestFile);
                RunCurlUpload(User, Pw, manifestPath, remotePath);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="user"></param>
        /// <param name="pw"></param>
        /// <param name="localArtifact"></param>
        /// <param name="remotePath"></param>
        private static void RunCurlUpload(string user, string pw, string localArtifact, string remotePath)
        {
            const string exeName = "curl.exe";
            string commandLine = "-v -X PUT -u" + user + ":" + pw + " -T \"" + localArtifact + "\" " + remotePath;

            Process process = ProcessRunner.RunProcess(exeName, commandLine);

            if (process.HasExited)
            {
                Console.WriteLine("Process has exited.");
                if (process.ExitCode != 0)
                {
                    Console.WriteLine("return code : {0", process.ExitCode);
                }
            }
            else
            {
                Console.WriteLine("Process has failed to exit.");
            }

        }

    }
}


       








   
