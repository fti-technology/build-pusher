using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using SQLite.Net.Attributes;
using SQLite.Net;
using SQLite.Net.Async;
using SQLite.Net.Interop;
using SQLite.Net.Platform.Win32;
using SQLiteNetExtensionsAsync.Extensions;

namespace TFSDriverLogic.Data
{
    public class DataBaseMgr
    {
        private readonly SQLiteConnectionString _sqLiteConnectionParam;
        private readonly SQLiteAsyncConnection _dbConnection;
        private readonly SQLiteConnectionPool _sqLiteConnectionPool;

        public DataBaseMgr(string dbPath)
        {
            _sqLiteConnectionParam = new SQLiteConnectionString(dbPath, false);
            _sqLiteConnectionPool = new SQLiteConnectionPool(new SQLitePlatformWin32());
            _dbConnection = GetConnection();

        }

        private SQLiteAsyncConnection GetConnection()
        {
            return new SQLiteAsyncConnection(()=>_sqLiteConnectionPool.GetConnection(_sqLiteConnectionParam));
        }

        public async Task Init()
        {
            await _dbConnection.CreateTablesAsync(new Type[] { typeof(PackageArtifactData), typeof(ArtifactDetail) });
        }

        public async Task InsertRecord(PackageArtifactData artifactData)
        {
            await _dbConnection.InsertAsync(artifactData);
        }

        public async Task UpdateWithChildren(ArtifactDetail artifactDetail)
        {
            await _dbConnection.UpdateWithChildrenAsync(artifactDetail);
        }

        public async Task UpdateWithChildren(PackageArtifactData packageArtifactData)
        {
            await _dbConnection.UpdateWithChildrenAsync(packageArtifactData);
        }
        public async Task Update(PackageArtifactData packageArtifactData)
        {
            await _dbConnection.UpdateAsync(packageArtifactData);
        }

        public async Task InsertRecordWithChildren(PackageArtifactData packageArtifactData)
        {
            await _dbConnection.InsertWithChildrenAsync(packageArtifactData, true);
        }

        public async Task<PackageArtifactData> QueryByProjBranchVer(string project, string branch, string subProject, string version)
        {
            var query = _dbConnection.Table<PackageArtifactData>().Where(v => v.Project == project && v.Branch == branch && v.SubProject == subProject && v.Version == version);

            return await query.FirstOrDefaultAsync();
        }

        public async Task<PackageArtifactData> QueryByProjBranch(string project, string branch, string subProject)
        {
            var query = _dbConnection.Table<PackageArtifactData>().Where(v => v.Project == project && v.Project == project && v.SubProject == subProject);

            return await query.FirstOrDefaultAsync();
        }

        public async Task<PackageArtifactData> QueryWithChild(int id)
        {
            return await _dbConnection.FindWithChildrenAsync<PackageArtifactData>(id, true);
        }

        public async Task<ArtifactDetail> QueryWithChildDetail(int id)
        {
            return await _dbConnection.FindWithChildrenAsync<ArtifactDetail>(id, true);
        }

        public async Task<PackageArtifactData> QueryWithChild(string project, string branch, string subProject)
        {
            var query = QueryByProjBranch(branch, branch, subProject);
            query.Wait();

            if (query.Result != null)
            {
                return await _dbConnection.FindWithChildrenAsync<PackageArtifactData>(query.Result.Id, true);
            }

            return null;
        }

        public async Task<PackageArtifactData> QueryWithChild(string project, string branch, string subProject, string version)
        {
            var query = QueryByProjBranchVer(project, branch, subProject, version);
            query.Wait();

            if (query.Result != null)
            {
                return await _dbConnection.FindWithChildrenAsync<PackageArtifactData>(query.Result.Id, true);
            }

            return null;
        }

    }
}
