using System;
using System.IO;
using System.Linq;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class FabricationLookupService
{
    public static FabricationSheet? FindFabricationByName(string fileName)
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var lowerFileName = (fileName ?? "").ToLowerInvariant();
            var filter = Builders<BsonDocument>.Filter.Eq("fileName", lowerFileName);
            var doc = col.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
            return doc == null ? null : BackendUtils.BsonDocToFabricationSheet(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindFabricationByName MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static FabricationSheet? FindFabricationByPath(string fullPath)
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
            var doc = col.Find(filter).FirstOrDefault();
            if (doc != null) return BackendUtils.BsonDocToFabricationSheet(doc);

            var fileName = Path.GetFileName(fullPath)?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(fileName))
            {
                filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
                doc = col.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
                if (doc != null) return BackendUtils.BsonDocToFabricationSheet(doc);
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindFabricationByPath MongoDB error: {ex.Message}");
            return null;
        }
    }
}
