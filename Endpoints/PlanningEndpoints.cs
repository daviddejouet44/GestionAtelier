using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// Planification intelligente (v1)
// POST /api/planning/optimize     — propose le meilleur ordre + gain chiffré
// POST /api/planning/apply-order  — applique l'ordre proposé (horaires machine)
// ======================================================
public static class PlanningEndpoints
{
    // Représentation légère d'un OF candidat au regroupement/ordonnancement.
    private class OfCandidate
    {
        public string FileName = "";
        public string NumeroDossier = "";
        public string Client = "";
        public string Moteur = "";
        public string Papier = "";
        public string Format = "";
        public int Quantite;
        public int TempsMin;
        public string Day = "";              // yyyy-MM-dd (jour d'impression)
        public string? CurrentMachineTime;   // HH:mm ou null
        // Champs calculés lors de l'application
        public string AssignedTime = "";
        public int SetupMinutes;
        public bool IsNewSetup;
        public int GroupIndex;
    }

    public static void MapPlanningEndpoints(this WebApplication app)
    {
        app.MapPost("/api/planning/optimize", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var (moteurs, startDate, endDate, fileNames) = await ReadFiltersAsync(ctx);
                var candidates = LoadCandidates(moteurs, startDate, endDate, fileNames);

                if (candidates.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucun OF à planifier pour ces critères (vérifiez qu'ils ont une date d'impression)." });

                var costCfg = MongoDbHelper.GetSettings<ChangeoverCostSettings>("changeoverCosts") ?? new ChangeoverCostSettings();
                int workStartMin = GetWorkStartMinutes();

                var machinesOut = new List<object>();
                int totCurCalages = 0, totOptCalages = 0, totCurMin = 0, totOptMin = 0;

                // Un tirage/calage est propre à chaque machine (elles tournent en parallèle).
                foreach (var grp in candidates.GroupBy(c => c.Moteur).OrderBy(g => g.Key))
                {
                    var moteur = grp.Key;
                    var cost = costCfg.EffectiveFor(moteur);
                    var list = grp.ToList();

                    // Ordre actuel : par horaire machine manuel (sinon N° dossier).
                    var currentOrder = list
                        .OrderBy(c => c.CurrentMachineTime ?? "99:99", StringComparer.Ordinal)
                        .ThenBy(c => c.NumeroDossier, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Ordre optimisé : regroupé par (papier, format), groupes ordonnés
                    // papier puis format (deux groupes voisins de même papier ne coûtent
                    // qu'un changement de format).
                    var optimizedOrder = list
                        .OrderBy(c => c.Papier, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.Format, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.NumeroDossier, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var (curMin, curCalages) = ComputeSetups(currentOrder, cost, assign: false, workStartMin);
                    var (optMin, optCalages) = ComputeSetups(optimizedOrder, cost, assign: true, workStartMin);

                    // Assignation des horaires par jour (on ne déplace pas les OF de jour).
                    AssignTimesPerDay(optimizedOrder, cost, workStartMin);

                    // Index de groupe (bandes de couleur côté UI).
                    var groupKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in optimizedOrder)
                    {
                        var key = c.Papier + "|" + c.Format;
                        if (!groupKeys.TryGetValue(key, out var idx))
                        {
                            idx = groupKeys.Count;
                            groupKeys[key] = idx;
                        }
                        c.GroupIndex = idx;
                    }

                    var groups = optimizedOrder
                        .GroupBy(c => new { c.Papier, c.Format })
                        .Select(g => new
                        {
                            papier = string.IsNullOrWhiteSpace(g.Key.Papier) ? "(non renseigné)" : g.Key.Papier,
                            format = string.IsNullOrWhiteSpace(g.Key.Format) ? "(non renseigné)" : g.Key.Format,
                            count = g.Count()
                        })
                        .ToList();

                    machinesOut.Add(new
                    {
                        moteur = string.IsNullOrWhiteSpace(moteur) ? "(sans moteur)" : moteur,
                        currentCalages = curCalages,
                        optimizedCalages = optCalages,
                        calagesSaved = Math.Max(0, curCalages - optCalages),
                        currentSetupMinutes = curMin,
                        optimizedSetupMinutes = optMin,
                        minutesSaved = Math.Max(0, curMin - optMin),
                        ofCount = list.Count,
                        groups,
                        order = optimizedOrder.Select(c => new
                        {
                            fileName = c.FileName,
                            numeroDossier = c.NumeroDossier,
                            client = c.Client,
                            papier = c.Papier,
                            format = c.Format,
                            quantite = c.Quantite,
                            tempsMin = c.TempsMin,
                            day = c.Day,
                            assignedDate = c.Day,
                            assignedTime = c.AssignedTime,
                            setupMinutes = c.SetupMinutes,
                            isNewSetup = c.IsNewSetup,
                            groupIndex = c.GroupIndex
                        }).ToList()
                    });

                    totCurCalages += curCalages;
                    totOptCalages += optCalages;
                    totCurMin += curMin;
                    totOptMin += optMin;
                }

                var summary = new
                {
                    ofCount = candidates.Count,
                    machineCount = machinesOut.Count,
                    currentCalages = totCurCalages,
                    optimizedCalages = totOptCalages,
                    calagesSaved = Math.Max(0, totCurCalages - totOptCalages),
                    currentSetupMinutes = totCurMin,
                    optimizedSetupMinutes = totOptMin,
                    minutesSaved = Math.Max(0, totCurMin - totOptMin)
                };

                return Results.Json(new { ok = true, summary, machines = machinesOut });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPost("/api/planning/apply-order", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (!json.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                    return Results.Json(new { ok = false, error = "items requis" });

                var fabCol = MongoDbHelper.GetFabricationsCollection();
                int updated = 0, skippedLocked = 0;

                foreach (var item in itemsEl.EnumerateArray())
                {
                    var fileName = item.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
                    var time     = item.TryGetProperty("assignedTime", out var t) ? t.GetString() ?? "" : "";
                    var dateStr  = item.TryGetProperty("assignedDate", out var d) ? d.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(time)) continue;

                    var filter = GestionAtelier.Endpoints.Fabrication.FabricationCrudEndpoints.BuildFileNameFilter(fileName);
                    var doc = fabCol.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
                    if (doc == null) continue;

                    // Respecte les OF verrouillés (position figée).
                    if (doc.Contains("locked") && doc["locked"] != BsonNull.Value
                        && doc["locked"].BsonType == BsonType.Boolean && doc["locked"].AsBoolean)
                    { skippedLocked++; continue; }

                    BsonDocument mpt = doc.Contains("manualPlanningTimes") && doc["manualPlanningTimes"] != BsonNull.Value
                        && doc["manualPlanningTimes"].IsBsonDocument
                        ? doc["manualPlanningTimes"].AsBsonDocument.DeepClone().AsBsonDocument
                        : new BsonDocument();

                    mpt["machineTime"] = time;
                    if (!string.IsNullOrWhiteSpace(dateStr)) mpt["machineDate"] = dateStr;

                    var updateDef = Builders<BsonDocument>.Update.Set("manualPlanningTimes", mpt);
                    if (DateTime.TryParse(dateStr, out var pd))
                        updateDef = updateDef.Set("dateImpression", new BsonDateTime(DateTime.SpecifyKind(pd.Date, DateTimeKind.Utc)));

                    await fabCol.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), updateDef);
                    updated++;
                }

                return Results.Json(new { ok = true, updated, skippedLocked });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static async Task<(List<string> moteurs, DateTime? start, DateTime? end, List<string> fileNames)> ReadFiltersAsync(HttpContext ctx)
    {
        var moteurs = new List<string>();
        var fileNames = new List<string>();
        DateTime? start = null, end = null;
        try
        {
            var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("moteurs", out var mEl) && mEl.ValueKind == JsonValueKind.Array)
                    moteurs = mEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
                if (json.TryGetProperty("fileNames", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
                    fileNames = fEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
                if (json.TryGetProperty("startDate", out var sEl) && DateTime.TryParse(sEl.GetString(), out var sd))
                    start = DateTime.SpecifyKind(sd.Date, DateTimeKind.Utc);
                if (json.TryGetProperty("endDate", out var eEl) && DateTime.TryParse(eEl.GetString(), out var ed))
                    end = DateTime.SpecifyKind(ed.Date.AddDays(1), DateTimeKind.Utc);
            }
        }
        catch { /* corps optionnel */ }
        return (moteurs, start, end, fileNames);
    }

    private static List<OfCandidate> LoadCandidates(List<string> moteurs, DateTime? start, DateTime? end, List<string> fileNames)
    {
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var conditions = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Exists("dateImpression"),
            Builders<BsonDocument>.Filter.Ne("dateImpression", BsonNull.Value),
            Builders<BsonDocument>.Filter.Ne("excludeFromPlanning", true),
            Builders<BsonDocument>.Filter.Ne("locked", true)
        };
        if (start.HasValue && end.HasValue)
        {
            conditions.Add(Builders<BsonDocument>.Filter.Gte("dateImpression", new BsonDateTime(start.Value)));
            conditions.Add(Builders<BsonDocument>.Filter.Lt("dateImpression", new BsonDateTime(end.Value)));
        }
        var docs = fabCol.Find(Builders<BsonDocument>.Filter.And(conditions)).ToList();

        var moteurSet = new HashSet<string>(moteurs, StringComparer.OrdinalIgnoreCase);
        var fileSet = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);

        var result = new List<OfCandidate>();
        foreach (var doc in docs)
        {
            string S(string f) => doc.Contains(f) && doc[f] != BsonNull.Value && doc[f].IsString ? doc[f].AsString.Trim() : "";
            int I(string f)
            {
                try { return doc.Contains(f) && doc[f] != BsonNull.Value ? doc[f].ToInt32() : 0; }
                catch { return 0; }
            }

            var moteur = S("moteurImpression");
            if (moteurSet.Count > 0 && !moteurSet.Contains(moteur)) continue;

            var fileName = S("fileName");
            if (fileSet.Count > 0 && !fileSet.Contains(fileName)) continue;
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            // Papier : media1 en priorité, sinon champ legacy "papier".
            var papier = S("media1");
            if (string.IsNullOrWhiteSpace(papier)) papier = S("papier");
            // Format : formatFeuille (format en machine) en priorité, sinon "format".
            var format = S("formatFeuille");
            if (string.IsNullOrWhiteSpace(format)) format = S("format");

            int temps = I("tempsProduitMinutes");
            if (temps <= 0) temps = 30;

            string day = "";
            try { day = doc["dateImpression"].ToUniversalTime().ToString("yyyy-MM-dd"); } catch { }

            string? curTime = null;
            if (doc.Contains("manualPlanningTimes") && doc["manualPlanningTimes"].IsBsonDocument)
            {
                var mpt = doc["manualPlanningTimes"].AsBsonDocument;
                if (mpt.Contains("machineTime") && mpt["machineTime"] != BsonNull.Value && mpt["machineTime"].IsString)
                    curTime = mpt["machineTime"].AsString;
            }

            result.Add(new OfCandidate
            {
                FileName = fileName,
                NumeroDossier = S("numeroDossier"),
                Client = S("client"),
                Moteur = moteur,
                Papier = papier,
                Format = format,
                Quantite = I("quantite"),
                TempsMin = temps,
                Day = day,
                CurrentMachineTime = curTime
            });
        }
        return result;
    }

    /// <summary>Coût de calage entre deux OF consécutifs (0 si même papier ET même format).</summary>
    private static int SetupCost((int calage, int papier, int format) cost, OfCandidate? prev, OfCandidate cur)
    {
        if (prev == null) return cost.calage + cost.papier + cost.format; // chargement initial complet
        bool samePaper = string.Equals(prev.Papier, cur.Papier, StringComparison.OrdinalIgnoreCase);
        bool sameFormat = string.Equals(prev.Format, cur.Format, StringComparison.OrdinalIgnoreCase);
        if (samePaper && sameFormat) return 0;
        return cost.calage + (samePaper ? 0 : cost.papier) + (sameFormat ? 0 : cost.format);
    }

    /// <summary>Somme des minutes de calage et nombre de calages pour une séquence (par machine).</summary>
    private static (int minutes, int calages) ComputeSetups(List<OfCandidate> seq, (int calage, int papier, int format) cost, bool assign, int workStartMin)
    {
        int minutes = 0, calages = 0;
        OfCandidate? prev = null;
        foreach (var c in seq)
        {
            int s = SetupCost(cost, prev, c);
            minutes += s;
            if (s > 0) calages++;
            if (assign) { c.SetupMinutes = s; c.IsNewSetup = s > 0; }
            prev = c;
        }
        return (minutes, calages);
    }

    /// <summary>Assigne un horaire séquentiel par (machine, jour) en suivant l'ordre optimisé.</summary>
    private static void AssignTimesPerDay(List<OfCandidate> optimizedOrder, (int calage, int papier, int format) cost, int workStartMin)
    {
        foreach (var dayGroup in optimizedOrder.GroupBy(c => c.Day))
        {
            int cursor = workStartMin;
            OfCandidate? prev = null;
            foreach (var c in dayGroup) // conserve l'ordre optimisé au sein du jour
            {
                int s = SetupCost(cost, prev, c);
                cursor += s;
                c.AssignedTime = MinutesToHhmm(cursor);
                cursor += c.TempsMin;
                prev = c;
            }
        }
    }

    private static int GetWorkStartMinutes()
    {
        try
        {
            var sched = MongoDbHelper.GetSettings<ScheduleSettings>("schedule");
            var ws = sched?.WorkStart;
            if (!string.IsNullOrWhiteSpace(ws) && ws.Contains(':'))
            {
                var parts = ws.Split(':');
                if (int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                    return Math.Clamp(h, 0, 23) * 60 + Math.Clamp(m, 0, 59);
            }
        }
        catch { }
        return 8 * 60; // 08:00 par défaut
    }

    private static string MinutesToHhmm(int minutes)
    {
        if (minutes < 0) minutes = 0;
        if (minutes > 23 * 60 + 59) minutes = 23 * 60 + 59; // borne journalière (v1)
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", minutes / 60, minutes % 60);
    }
}
