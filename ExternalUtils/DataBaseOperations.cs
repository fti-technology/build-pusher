using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using PusherDataLogging.Entities;
using PusherDataLogging.Properties;

namespace PusherDataLogging
{
    internal class DataBaseOperationsContext
    {
        private readonly IMongoDatabase database;

        public DataBaseOperationsContext()
		{
			var client = new MongoClient(Settings.Default.ConnectionString);

			database = client.GetDatabase(Settings.Default.DataBaseName);
		}

		public IMongoCollection<LogEntry> LogCollection
		{
			get
			{
                return database.GetCollection<LogEntry>("logentry");
			}
		}
    }
}
