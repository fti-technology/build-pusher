using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Common;

namespace BuildDataDriver.tools
{
    public class RestUploadManager
    {
        // Sample default URL pointing to api
        private string _baseUrl = "http://localhost/FTIUploadServer/api/";

        public RestUploadManager(string baseUrl, string APIVersion)
        {
            //if(Path.GetDirectoryName() != "api")
             //TODO?

            if (!baseUrl.EndsWith("/"))
                baseUrl = baseUrl + "/";

            _baseUrl = baseUrl;

            if (!baseUrl.EndsWith(APIVersion))
                _baseUrl = _baseUrl + APIVersion + "/";

        }

        /// <summary>
        /// Checks for existence of a subdir. This is typically checking for /BranchDir/SubDir
        /// </summary>
        /// <param name="branchName">Parent directory name - typically correlates to the branch name.</param>
        /// <param name="versionName">Sub directory name - typically correlates to the directory name that makes the version ( i.e. 20150209.2) </param>
        /// <returns>true/false</returns>
        public bool VersionDirectoryExists(string branchName, string versionName)
        {
            var ret = GetVersionDirectoryPath(branchName);
            ret.Wait();
            return ret.Result.Contains(versionName);
        }

        /// <summary>
        /// Checks for existence of a parent directory. This is typically checking for /BranchDir/
        /// </summary>
        /// <param name="branchName">Parent directory name - typically correlates to the branch name.</param>
        /// <returns>true/false</returns>
        public bool BranchDirectoryExists(string branchName)
        {
            var ret = GetBranchDirectoryNames();
            ret.Wait();
            return ret.Result.Contains(branchName);
        }

        /// <summary>
        /// Creates a directory given the branchDir.
        /// </summary>
        /// <param name="path">Path of directory to create on server</param>
        /// <returns>Task</returns>
        public async Task CreateDirectory(string path)
        {
            using (var client = new HttpClient())
            {
                var httpContent = new StringContent(path, Encoding.UTF8);
                client.BaseAddress = new Uri(_baseUrl);
                var response = await client.PutAsync("Directory?path=" + path, httpContent);
                response.EnsureSuccessStatusCode();
            }

        }

        /// <summary>
        /// Creates a directory given the branchDir.
        /// </summary>
        /// <param name="branchDir">Branch or parent directory</param>
        /// <param name="versionDir">Version or sub directory created under branchDir</param>
        /// <returns>Task</returns>
        public async Task CreateDirectory(string branchDir, string versionDir)
        {
            using (var client = new HttpClient())
            {
                versionDir = versionDir.TrimStart(Path.AltDirectorySeparatorChar);
                branchDir = branchDir.TrimEnd(Path.AltDirectorySeparatorChar);
                var path = branchDir + '/' + versionDir;
                await CreateDirectory(path);
            }

        }

        /// <summary>
        /// Deletes a directory given the branchDir
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task DeleteDirectory(string path)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_baseUrl);
                var response = await client.DeleteAsync("Directory?path=" + path);
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// Creates a directory given the Branch and Version
        /// </summary>
        /// <param name="branchDir"></param>
        /// <param name="versionDir"></param>
        /// <returns></returns>
        public async Task DeleteDirectory(string branchDir, string versionDir)
        {
            using (var client = new HttpClient())
            {
                versionDir = versionDir.TrimStart(Path.AltDirectorySeparatorChar);
                branchDir = branchDir.TrimEnd(Path.AltDirectorySeparatorChar);
                var path = branchDir + '/' + versionDir;
                await DeleteDirectory(path);
            }
        }

        /// <summary>
        /// Get a listing of all Top-level Directories that would essentially correspond to the Branch level dirs
        /// </summary>
        /// <returns>list of directory branchDir names </returns>
        public async Task<IEnumerable<string>> GetBranchDirectoryNames()
        {
            string stringReturnValue = null;
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_baseUrl);

                HttpResponseMessage response = await client.GetAsync("Directory");
                // Check that response was successful or throw exception
                response.EnsureSuccessStatusCode();
                stringReturnValue = await response.Content.ReadAsStringAsync();
            }
            if (string.IsNullOrEmpty(stringReturnValue))
                return Enumerable.Empty<string>();

            return CleanUpArrayString(stringReturnValue);
        }

        /// <summary>
        /// Get a listing of all Top-level Directories that would essentially correspond to the Branch level dirs
        /// </summary>
        /// <returns>list of directory branchDir names - could be in the form of "BRANCH/VERSION"</returns>
        public async Task<IEnumerable<string>> GetVersionDirectoryPath(string subPath)
        {
            var query = await GetDirectoryTask(subPath, "Directory");
            return query;
        }

        
        public async Task<IEnumerable<string>> GetVersionDirectoryNames(string subPath)
        {
            var query = await GetDirectoryTask(subPath, "GetDirectoryNames");
            return query;
        }


        private async Task<IEnumerable<string>> GetDirectoryTask(string subPath, string API)
        {
            if (string.IsNullOrEmpty(subPath))
                return Enumerable.Empty<string>();

            string stringReturnValue = null;
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_baseUrl);

                HttpResponseMessage response = await client.GetAsync(API + "?path=" + subPath);
                // Check that response was successful or throw exception
                response.EnsureSuccessStatusCode();
                stringReturnValue = await response.Content.ReadAsStringAsync();
            }
            if (string.IsNullOrEmpty(stringReturnValue))
                return Enumerable.Empty<string>();

            return CleanUpArrayString(stringReturnValue);
        }



        private List<string> CleanUpArrayString(string stringReturnValue)
        {
            if (string.IsNullOrEmpty(stringReturnValue))
                return new List<string>();

            stringReturnValue = stringReturnValue.Replace("\"", "").TrimStart('[').TrimEnd(']');
            if (string.IsNullOrEmpty(stringReturnValue) || string.IsNullOrWhiteSpace(stringReturnValue))
                return new List<string>();

            return stringReturnValue.Split(',').ToList();
        }

        /// <summary>
        /// Get a listing of all sub directories give a branchDir(parent) and versionDir(sub)
        /// </summary>
        /// <returns>list of file name in the directory</returns>
        public async Task<IEnumerable<string>> GetFileList(string branchDir, string versionDir)
        {
            string stringReturnValue = null;
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_baseUrl);

                HttpResponseMessage response = await client.GetAsync("Files/" + branchDir + "/" + versionDir + "/");
                // Check that response was successful or throw exception
                response.EnsureSuccessStatusCode();
                stringReturnValue = await response.Content.ReadAsStringAsync();
            }
            if (string.IsNullOrEmpty(stringReturnValue))
                return Enumerable.Empty<string>();

            return CleanUpArrayString(stringReturnValue);
        }


        /// <summary>
        /// Upload a file to the specified remote Path
        /// </summary>
        /// <param name="strFileToUpload">Local file path</param>
        /// <param name="remotePath">remote path - typically in the form BRANCH/VERSION</param>
        /// <returns></returns>
        public string Uploader(string strFileToUpload, string remotePath)
        {
            Uri oUri = new Uri(_baseUrl + "Upload?path=" + remotePath);
            string strBoundary = "----------" + DateTime.Now.Ticks.ToString("x");

            // The trailing boundary string
            byte[] boundaryBytes = Encoding.ASCII.GetBytes("\r\n--" + strBoundary + "--\r\n");

            // The post message header
            StringBuilder sb = new StringBuilder();
            sb.Append("--");
            sb.Append(strBoundary);
            sb.Append("\r\n");
            sb.Append("Content-Disposition: form-data; name=\"");
            sb.Append(Path.GetFileName(strFileToUpload));
            sb.Append("\"; filename=\"");
            sb.Append(Path.GetFileName(strFileToUpload));
            sb.Append("\"; id=\"");
            sb.Append(Path.GetFileName(strFileToUpload));
            sb.Append("\"; type=\"file\"");
            sb.Append("\r\n");
            sb.Append("Content-Type: ");
            sb.Append("application/octet-stream");
            sb.Append("\r\n");
            sb.Append("\r\n");
            string strPostHeader = sb.ToString();
            byte[] postHeaderBytes = Encoding.UTF8.GetBytes(strPostHeader);

            // The WebRequest
            HttpWebRequest oWebrequest = (HttpWebRequest)WebRequest.Create(oUri);
            oWebrequest.ContentType = "multipart/form-data; boundary=" + strBoundary;
            oWebrequest.Method = "POST";
            oWebrequest.KeepAlive = false;
            oWebrequest.Timeout = System.Threading.Timeout.Infinite;
            oWebrequest.ProtocolVersion = HttpVersion.Version10;

            // This is important, otherwise the whole file will be read to memory anyway...
            oWebrequest.AllowWriteStreamBuffering = false;            

            // Get a FileStream and set the final properties of the WebRequest
            FileStream oFileStream = new FileStream(strFileToUpload, FileMode.Open, FileAccess.Read);

            long length = postHeaderBytes.Length + oFileStream.Length + boundaryBytes.Length;
            oWebrequest.ContentLength = length;
            Stream oRequestStream = oWebrequest.GetRequestStream();

            // Write the post header
            oRequestStream.Write(postHeaderBytes, 0, postHeaderBytes.Length);

            // Stream the file contents in small pieces (4096 bytes, max).
            byte[] buffer = new Byte[checked((uint)Math.Min(4096, (int)oFileStream.Length))];
            int bytesRead = 0;
            while ((bytesRead = oFileStream.Read(buffer, 0, buffer.Length)) != 0)
                oRequestStream.Write(buffer, 0, bytesRead);

            // Add the trailing boundary
            oRequestStream.Write(boundaryBytes, 0, boundaryBytes.Length);
            var responseAsync = oWebrequest.GetResponseAsync();
            responseAsync.Wait();
            //WebResponse oWResponse = oWebrequest.GetResponse();
            WebResponse oWResponse = responseAsync.Result;
            Stream s = oWResponse.GetResponseStream();
            StreamReader sr = new StreamReader(s);
            String sReturnString = sr.ReadToEnd();

            // Clean up
            oFileStream.Close();
            oRequestStream.Close();
            s.Close();
            sr.Close();

            return sReturnString;
        }
    }
}