using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class MongoDbHelper
{
    private static readonly string ConnectionString = "mongodb://localhost:27017";
    private static readonly string DatabaseName = "GestionAtelier";
    private static readonly string CountersCollection = "counters";

    public static IMongoClient GetClient() => new MongoClient(ConnectionString);

    public static IMongoDatabase GetDatabase() => GetClient().GetDatabase(DatabaseName);

    public static long GetNextFileNumber()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "file_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB counter error: {ex.Message}");
            return 1;
        }
    }

    public static long GetNextUserId()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "user_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB user counter error: {ex.Message}");
            return 1;
        }
    }

    public static IMongoCollection<BsonDocument> GetUsersCollection()
        => GetDatabase().GetCollection<BsonDocument>("users");

    public static IMongoCollection<BsonDocument> GetDeliveriesCollection()
        => GetDatabase().GetCollection<BsonDocument>("deliveries");

    public static IMongoCollection<BsonDocument> GetFabricationsCollection()
        => GetDatabase().GetCollection<BsonDocument>("fabrications");

    public static IMongoCollection<BsonDocument> GetAssignmentsCollection()
        => GetDatabase().GetCollection<BsonDocument>("assignments");

    public static IMongoCollection<BsonDocument> GetSettingsCollection()
        => GetDatabase().GetCollection<BsonDocument>("settings");

    public static IMongoCollection<BsonDocument> GetLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("logs");

    public static IMongoCollection<T> GetCollection<T>(string name)
        => GetDatabase().GetCollection<T>(name);

    public static T? GetSettings<T>(string settingsId) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            var doc = col.Find(filter).FirstOrDefault();
            if (doc == null) return null;
            doc.Remove("_id");
            return JsonSerializer.Deserialize<T>(doc.ToJson());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetSettings({settingsId}) error: {ex.Message}");
            return null;
        }
    }

    public static void UpsertSettings<T>(string settingsId, T value) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var json = JsonSerializer.Serialize(value);
            var doc = BsonDocument.Parse(json);
            doc["_id"] = settingsId;
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] UpsertSettings({settingsId}) error: {ex.Message}");
        }
    }

    public static void DeleteSettings(string settingsId)
    {
        try
        {
            var col = GetSettingsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            col.DeleteOne(filter);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] DeleteSettings({settingsId}) error: {ex.Message}");
        }
    }

    public static IMongoCollection<BsonDocument> GetActivityLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("activity_logs");

    public static IMongoCollection<BsonDocument> GetPrintEnginesCollection()
        => GetDatabase().GetCollection<BsonDocument>("print_engines");

    public static void InsertActivityLog(ActivityLogEntry entry)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"] = entry.Timestamp,
                ["userLogin"] = entry.UserLogin,
                ["userName"]  = entry.UserName,
                ["action"]    = entry.Action,
                ["details"]   = entry.Details
            };
            col.InsertOne(doc);
        }
        catch (Exception ex) { Console.WriteLine($"[WARN] Activity log failed: {ex.Message}"); }
    }

    public static List<object> GetActivityLogs(string? dateFilter, int limit = 500)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp = d.Contains("timestamp") ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                userLogin = d.Contains("userLogin") ? d["userLogin"].AsString : "",
                userName  = d.Contains("userName")  ? d["userName"].AsString  : "",
                action    = d.Contains("action")    ? d["action"].AsString    : "",
                details   = d.Contains("details")   ? d["details"].AsString   : ""
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetActivityLogs error: {ex.Message}");
            return new();
        }
    }

    public static List<string> GetPrintEngines()
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var docs = col.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("name"))
                .ToList();
            return docs.Select(d => d.Contains("name") ? d["name"].AsString : "").Where(s => s.Length > 0).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetPrintEngines error: {ex.Message}");
            return new();
        }
    }

    public static List<object> GetPrintEnginesWithIp()
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var docs = col.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("name"))
                .ToList();
            return docs
                .Where(d => d.Contains("name") && !string.IsNullOrWhiteSpace(d["name"].AsString))
                .Select(d => (object)new {
                    name = d["name"].AsString,
                    ip   = d.Contains("ip") ? d["ip"].AsString : ""
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetPrintEnginesWithIp error: {ex.Message}");
            return new();
        }
    }

    public static void AddPrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var existing = col.Find(filter).FirstOrDefault();
            if (existing == null)
            {
                col.InsertOne(new BsonDocument { ["name"] = name, ["ip"] = "" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] AddPrintEngine error: {ex.Message}");
        }
    }

    public static void AddPrintEngineWithIp(string name, string ip)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var update = Builders<BsonDocument>.Update
                .Set("name", name)
                .Set("ip", ip ?? "");
            col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] AddPrintEngineWithIp error: {ex.Message}");
        }
    }

    public static void RemovePrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            col.DeleteOne(Builders<BsonDocument>.Filter.Eq("name", name));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] RemovePrintEngine error: {ex.Message}");
        }
    }

    public static void InsertLog(LogEntry entry)
    {
        try
        {
            var col = GetLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"]  = entry.Timestamp,
                ["method"]     = entry.Method,
                ["path"]       = entry.Path,
                ["statusCode"] = entry.StatusCode
            };
            col.InsertOne(doc);
        }
        catch { /* ignore log errors */ }
    }

    public static List<object> GetRecentLogs(string? dateFilter, int limit = 200)
    {
        try
        {
            var col = GetLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp  = d.Contains("timestamp")  ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                method     = d.Contains("method")     ? d["method"].AsString : "",
                path       = d.Contains("path")       ? d["path"].AsString : "",
                statusCode = d.Contains("statusCode") ? d["statusCode"].AsInt32 : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetRecentLogs error: {ex.Message}");
            return new();
        }
    }
}
