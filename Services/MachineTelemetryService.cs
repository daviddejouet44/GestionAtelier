using System;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

// ======================================================
// Pilotage machines (point 8) — ingestion de télémétrie
// Point d'entrée commun aux connecteurs (push d'agent/passerelle JMF/SNMP
// ou pull HTTP par le service de polling). Met à jour l'état machine
// (machineStatus) et journalise les transitions (machineEvents).
// ======================================================
public static class MachineTelemetryService
{
    public static (bool ok, string? error) Ingest(
        string moteur, string? statut, long? compteurFeuilles, string? ofEnCours,
        int? tempsRestantMinutes, string? note, string source, string by)
    {
        if (string.IsNullOrWhiteSpace(moteur))
            return (false, "moteur requis");

        var col = MongoDbHelper.GetCollection<BsonDocument>("machineStatus");
        var filter = Builders<BsonDocument>.Filter.Eq("moteur", moteur);
        var prev = col.Find(filter).FirstOrDefault();
        var prevStatut = prev != null && prev.Contains("statut") && prev["statut"] != BsonNull.Value && prev["statut"].IsString
            ? prev["statut"].AsString : "";

        var updates = new System.Collections.Generic.List<UpdateDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Update.Set("moteur", moteur),
            Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow),
            Builders<BsonDocument>.Update.Set("updatedBy", string.IsNullOrWhiteSpace(by) ? source : by),
            Builders<BsonDocument>.Update.Set("lastTelemetryAt", DateTime.UtcNow),
            Builders<BsonDocument>.Update.Set("connectionSource", source)
        };

        string? newStatut = null;
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (!MachineStatuses.IsValid(statut))
                return (false, $"Statut invalide : {statut}");
            newStatut = MachineStatuses.All.First(x => string.Equals(x, statut, StringComparison.OrdinalIgnoreCase));
            updates.Add(Builders<BsonDocument>.Update.Set("statut", newStatut));
        }
        if (compteurFeuilles.HasValue)
            updates.Add(Builders<BsonDocument>.Update.Set("compteurFeuilles", Math.Max(0, compteurFeuilles.Value)));
        if (note != null)
            updates.Add(Builders<BsonDocument>.Update.Set("note", note));

        if (ofEnCours != null)
        {
            var ofFile = ofEnCours.Trim();
            updates.Add(Builders<BsonDocument>.Update.Set("ofEnCours", ofFile));
            string dossier = ""; int temps = 0;
            if (!string.IsNullOrWhiteSpace(ofFile))
            {
                var fabCol = MongoDbHelper.GetFabricationsCollection();
                var doc = fabCol.Find(GestionAtelier.Endpoints.Fabrication.FabricationCrudEndpoints.BuildFileNameFilter(ofFile))
                    .SortByDescending(x => x["_id"]).FirstOrDefault();
                if (doc != null)
                {
                    if (doc.Contains("numeroDossier") && doc["numeroDossier"] != BsonNull.Value && doc["numeroDossier"].IsString)
                        dossier = doc["numeroDossier"].AsString;
                    try { if (doc.Contains("tempsProduitMinutes") && doc["tempsProduitMinutes"] != BsonNull.Value) temps = doc["tempsProduitMinutes"].ToInt32(); } catch { }
                }
            }
            updates.Add(Builders<BsonDocument>.Update.Set("ofEnCoursDossier", dossier));
            updates.Add(Builders<BsonDocument>.Update.Set("tempsRestantMinutes",
                tempsRestantMinutes.HasValue ? Math.Max(0, tempsRestantMinutes.Value) : temps));
        }
        else if (tempsRestantMinutes.HasValue)
        {
            updates.Add(Builders<BsonDocument>.Update.Set("tempsRestantMinutes", Math.Max(0, tempsRestantMinutes.Value)));
        }

        // Journalise la transition de statut (base des KPI).
        if (newStatut != null && !string.Equals(prevStatut, newStatut, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                MongoDbHelper.GetCollection<BsonDocument>("machineEvents").InsertOne(new BsonDocument
                {
                    ["moteur"]         = moteur,
                    ["statut"]         = newStatut,
                    ["previousStatut"] = prevStatut,
                    ["note"]           = note ?? "",
                    ["at"]             = DateTime.UtcNow,
                    ["by"]             = string.IsNullOrWhiteSpace(by) ? source : by,
                    ["source"]         = source
                });
            }
            catch { /* non bloquant */ }
        }

        col.UpdateOne(filter, Builders<BsonDocument>.Update.Combine(updates), new UpdateOptions { IsUpsert = true });
        return (true, null);
    }
}
