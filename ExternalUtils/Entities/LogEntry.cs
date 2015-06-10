using System;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;

namespace PusherDataLogging.Entities
{
    public class LogEntry
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime ActionTime { get; set; }
        public string Branch { get; set; }
        public string Version { get; set; }
        public string Guid { get; set; }
        public string Message { get; set; }

        public LogEntry()
        {
            Id = ObjectId.GenerateNewId().ToString();
            JsonWriterSettings.Defaults.Indent = true;
        }
    }
}
