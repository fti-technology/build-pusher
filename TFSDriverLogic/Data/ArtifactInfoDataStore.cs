using System;
using System.Collections.Generic;
using SQLite.Net.Attributes;
using SQLiteNetExtensions.Attributes;

namespace TFSDriverLogic.Data
{
    public class PackageArtifactData
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        /// <summary>
        /// Root Source path of the Total package location
        /// </summary>
        public string SourcePath { get; set; }
        /// <summary>
        /// TFS Branch
        /// </summary>
        public string Branch { get; set; }
        /// <summary>
        /// TFS Project
        /// </summary>
        public string Project { get; set; }

        /// <summary>
        /// TFS sub project
        /// </summary>
        public string SubProject { get; set; }

        /// <summary>
        /// VERSION String YYYYMMDD.V
        /// </summary>
        public string Version { get; set; }
        /// <summary>
        /// Currently Deployed
        /// </summary>
        public bool Deployed { get; set; }
        /// <summary>
        /// Newest Package completion date from all of the packages
        /// </summary>
        public DateTime BuildCompletion { get; set; }
        /// <summary>
        /// Time this record was updated last or created
        /// </summary>
        public DateTime RecordTime { get; set; }
        /// <summary>
        /// Deployment Date
        /// </summary>
        public DateTime DeployedDate { get; set; }

        [OneToMany(CascadeOperations = CascadeOperation.All)]
        public List<ArtifactDetail> PackageDetails { get; set; }
    }

    public class ArtifactDetail
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// TFS Build Number
        /// </summary>
        public string BuildNumber { get; set; }     // 2015 RINGTAIL8 Packages_2015xxxx 
        /// <summary>
        /// Installation executable name
        /// </summary>
        public string PackageFullPath { get; set; }         // "RINGTAIL_2015 RINGTAIL8 PACKAGES_20150225.11.exe"

        /// <summary>
        /// Installation executable name
        /// </summary>
        public string PackageName { get; set; }         // "RINGTAIL_2015 RINGTAIL8 PACKAGES_20150225.11.exe"

        /// <summary>
        /// MD5 checksum for file
        /// </summary>
        public string Md5 { get; set; }

        /// <summary>
        /// Used to store the TFS Build System time for "Build Finished Time"
        /// </summary>
        public DateTime PackageCompletionTime { get; set; }

        [ForeignKey(typeof(PackageArtifactData))]
        public int PackageArtifactDataId { get; set; }
    }
}