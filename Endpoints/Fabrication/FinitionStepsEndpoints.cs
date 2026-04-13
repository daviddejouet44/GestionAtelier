using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Fabrication;

public static class FinitionStepsEndpoints
{
    // All 8 finition step keys in order
    public static readonly string[] AllStepKeys = new[]
    {
        "embellissement", "rainage", "pliage", "faconnage",
        "coupe", "emballage", "depart", "livraison"
    };

    public static void MapFinitionStepsEndpoints(this WebApplication app)
    {
// ======================================================
// GET /api/fabrication/{id}/finition-steps
// Returns the current finitionSteps state for a job
// ======================================================
app.MapGet("/api/fabrication/{id}/finition-steps", (string id) =>
{
    try
    {
        var col = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? doc = null;

        // id can be MongoDB ObjectId or fullPath/fileName
        if (ObjectId.TryParse(id, out var oid))
            doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", oid)).FirstOrDefault();

        if (doc == null)
            doc = col.Find(Builders<BsonDocument>.Filter.Eq("fullPath", id)).FirstOrDefault();
        if (doc == null)
            doc = col.Find(Builders<BsonDocument>.Filter.Eq("fileName", id)).SortByDescending(x => x["_id"]).FirstOrDefault();

        if (doc == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        var fs = BackendUtils.BsonDocToFinitionSteps(doc);

        return Results.Json(new { ok = true, finitionSteps = SerializeFinitionSteps(fs) });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// PUT /api/fabrication/{id}/finition-step
// Checks or unchecks a finition step, recording timestamp + operator
// Body: { step: "rainage", done: true, conditionnement?: "...", tracking?: "..." }
// ======================================================
app.MapPut("/api/fabrication/{id}/finition-step", async (string id, HttpContext ctx) =>
{
    try
    {
        // Get operator from auth token
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string operatorName = "Opérateur";
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length >= 1) operatorName = parts[0];
            }
            catch { }
        }

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("step", out var stepProp))
            return Results.Json(new { ok = false, error = "Propriété 'step' manquante" });

        var step = stepProp.GetString()?.ToLowerInvariant() ?? "";
        if (!AllStepKeys.Contains(step))
            return Results.Json(new { ok = false, error = $"Étape inconnue: {step}" });

        var done = json.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean();
        string? conditionnement = json.TryGetProperty("conditionnement", out var condProp) ? condProp.GetString() : null;
        string? tracking = json.TryGetProperty("tracking", out var trackProp) ? trackProp.GetString() : null;

        // Emballage requires conditionnement
        if (step == "emballage" && done && string.IsNullOrWhiteSpace(conditionnement))
            return Results.Json(new { ok = false, error = "Le conditionnement est obligatoire pour l'étape Emballage" });

        var col = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? doc = null;
        FilterDefinition<BsonDocument>? filter = null;

        if (ObjectId.TryParse(id, out var oid))
        {
            filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
            doc = col.Find(filter).FirstOrDefault();
        }
        if (doc == null)
        {
            filter = Builders<BsonDocument>.Filter.Eq("fullPath", id);
            doc = col.Find(filter).FirstOrDefault();
        }
        if (doc == null)
        {
            filter = Builders<BsonDocument>.Filter.Eq("fileName", id);
            doc = col.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
        }

        if (doc == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        // Build or update the finitionSteps subdoc
        BsonDocument fsDoc = doc.Contains("finitionSteps") && doc["finitionSteps"] != BsonNull.Value && doc["finitionSteps"].IsBsonDocument
            ? doc["finitionSteps"].AsBsonDocument.DeepClone().AsBsonDocument
            : new BsonDocument();

        // Ensure all keys exist
        foreach (var k in AllStepKeys)
        {
            if (!fsDoc.Contains(k) || fsDoc[k] == BsonNull.Value || !fsDoc[k].IsBsonDocument)
                fsDoc[k] = new BsonDocument { ["done"] = false, ["doneAt"] = BsonNull.Value, ["doneBy"] = BsonNull.Value };
        }

        var stepDoc = fsDoc[step].AsBsonDocument;
        stepDoc["done"] = done;
        if (done)
        {
            stepDoc["doneAt"] = DateTime.UtcNow;
            stepDoc["doneBy"] = operatorName;
        }
        else
        {
            stepDoc["doneAt"] = BsonNull.Value;
            stepDoc["doneBy"] = BsonNull.Value;
        }

        if (step == "emballage")
            stepDoc["conditionnement"] = conditionnement ?? (BsonValue)BsonNull.Value;
        if (step == "livraison")
            stepDoc["tracking"] = tracking ?? (BsonValue)BsonNull.Value;

        fsDoc[step] = stepDoc;

        // Update the document
        var realFilter = Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]);
        var update = Builders<BsonDocument>.Update.Set("finitionSteps", fsDoc);
        col.UpdateOne(realFilter, update);

        return Results.Json(new { ok = true, step, done, doneAt = done ? (object?)DateTime.UtcNow : null, doneBy = done ? operatorName : null });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});
    }

    /// <summary>
    /// Returns the list of step keys that are required (not auto-validated) for a given fabrication document.
    /// Steps not present in the fabrication sheet are considered non-applicable and automatically done.
    /// </summary>
    public static HashSet<string> GetRequiredSteps(BsonDocument fabDoc)
    {
        var required = new HashSet<string>(AllStepKeys);

        // Check which finitions are selected in the production sheet
        bool hasEnnoblissement = fabDoc.Contains("ennoblissement") && fabDoc["ennoblissement"] != BsonNull.Value
            && fabDoc["ennoblissement"].IsBsonArray && fabDoc["ennoblissement"].AsBsonArray.Count > 0;
        bool hasRainage = fabDoc.Contains("rainage") && fabDoc["rainage"] != BsonNull.Value
            && fabDoc["rainage"].BsonType == BsonType.Boolean && fabDoc["rainage"].AsBoolean;
        bool hasPlis = fabDoc.Contains("plis") && fabDoc["plis"] != BsonNull.Value
            && !string.IsNullOrWhiteSpace(fabDoc["plis"].AsString);
        bool hasFaconnageBinding = fabDoc.Contains("faconnageBinding") && fabDoc["faconnageBinding"] != BsonNull.Value
            && !string.IsNullOrWhiteSpace(fabDoc["faconnageBinding"].AsString);
        bool hasFaconnage = fabDoc.Contains("faconnage") && fabDoc["faconnage"] != BsonNull.Value
            && fabDoc["faconnage"].IsBsonArray && fabDoc["faconnage"].AsBsonArray.Count > 0;

        if (!hasEnnoblissement) required.Remove("embellissement");
        if (!hasRainage) required.Remove("rainage");
        if (!hasPlis) required.Remove("pliage");
        if (!hasFaconnageBinding && !hasFaconnage) required.Remove("faconnage");

        // coupe, emballage, depart, livraison are always required
        return required;
    }

    /// <summary>
    /// Checks if all required finition steps are done for this fabrication document.
    /// Returns a list of missing step keys, or empty list if all done.
    /// </summary>
    public static List<string> GetMissingSteps(BsonDocument fabDoc)
    {
        var required = GetRequiredSteps(fabDoc);

        BsonDocument fsDoc = fabDoc.Contains("finitionSteps") && fabDoc["finitionSteps"] != BsonNull.Value
            && fabDoc["finitionSteps"].IsBsonDocument
            ? fabDoc["finitionSteps"].AsBsonDocument
            : new BsonDocument();

        var missing = new List<string>();
        foreach (var key in required)
        {
            bool done = false;
            if (fsDoc.Contains(key) && fsDoc[key] != BsonNull.Value && fsDoc[key].IsBsonDocument)
            {
                var s = fsDoc[key].AsBsonDocument;
                done = s.Contains("done") && s["done"] != BsonNull.Value && s["done"].AsBoolean;
            }
            if (!done) missing.Add(key);
        }
        return missing;
    }

    private static object SerializeFinitionSteps(FinitionSteps fs)
    {
        return new
        {
            embellissement = SerializeStep(fs.Embellissement),
            rainage        = SerializeStep(fs.Rainage),
            pliage         = SerializeStep(fs.Pliage),
            faconnage      = SerializeStep(fs.Faconnage),
            coupe          = SerializeStep(fs.Coupe),
            emballage      = SerializeStepEmballage(fs.Emballage),
            depart         = SerializeStep(fs.Depart),
            livraison      = SerializeStepLivraison(fs.Livraison)
        };
    }

    private static object SerializeStep(FinitionStep s) => new
    {
        done   = s.Done,
        doneAt = s.DoneAt,
        doneBy = s.DoneBy
    };

    private static object SerializeStepEmballage(FinitionStep s) => new
    {
        done            = s.Done,
        doneAt          = s.DoneAt,
        doneBy          = s.DoneBy,
        conditionnement = s.Conditionnement
    };

    private static object SerializeStepLivraison(FinitionStep s) => new
    {
        done   = s.Done,
        doneAt = s.DoneAt,
        doneBy = s.DoneBy,
        tracking = s.Tracking
    };
}
