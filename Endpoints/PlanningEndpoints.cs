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
        // Priorité (point 2)
        public bool Urgent;
        public DateTime? DateImpression;
        public DateTime? DateReceptionSouhaitee;
        public DateTime? LastModifiedAt;
        public bool LastActionModification;
        public int PriorityScore;
        public List<string> PriorityReasons = new();
        public string PriorityLevel = "normal"; // normal | elevated | urgent
        // Contraintes (recalcul)
        public bool Blocked;
        public string BlockReason = "";
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

                var filters = await ReadFiltersAsync(ctx);
                var candidates = LoadCandidates(filters);

                if (candidates.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucun OF à planifier pour ces critères (vérifiez qu'ils ont une date d'impression)." });

                var costCfg = MongoDbHelper.GetSettings<ChangeoverCostSettings>("changeoverCosts") ?? new ChangeoverCostSettings();
                var prioCfg = MongoDbHelper.GetSettings<PriorityConfig>("priorityConfig") ?? new PriorityConfig();
                var vipSet = new HashSet<string>(prioCfg.VipClients ?? new(), StringComparer.OrdinalIgnoreCase);
                var brokenSet = new HashSet<string>(filters.BrokenMachines, StringComparer.OrdinalIgnoreCase);
                var stockOutSet = new HashSet<string>(filters.OutOfStockPapers, StringComparer.OrdinalIgnoreCase);
                // Machines déclarées indisponibles dans le suivi temps réel (point 3) : panne / maintenance.
                var autoUnavailable = LoadUnavailableMachines();
                int workStartMin = GetWorkStartMinutes();
                var today = DateTime.UtcNow;
                var now = DateTime.UtcNow;

                // Priorité + contraintes (recalcul) pour chaque OF.
                foreach (var c in candidates)
                {
                    ComputePriority(c, prioCfg, vipSet, today, now);
                    if (brokenSet.Contains(c.Moteur)) { c.Blocked = true; c.BlockReason = "Machine en panne"; }
                    else if (autoUnavailable.TryGetValue(c.Moteur, out var statut))
                    {
                        c.Blocked = true;
                        c.BlockReason = statut.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)
                            ? "Machine en maintenance" : "Machine en panne";
                    }
                    else if (stockOutSet.Contains(c.Papier)) { c.Blocked = true; c.BlockReason = "Rupture papier"; }
                }

                var machinesOut = new List<object>();
                var conflicts = new List<object>();
                int totCurCalages = 0, totOptCalages = 0, totCurMin = 0, totOptMin = 0, totBlocked = 0;

                // Un tirage/calage est propre à chaque machine (elles tournent en parallèle).
                foreach (var grp in candidates.GroupBy(c => c.Moteur).OrderBy(g => g.Key))
                {
                    var moteur = grp.Key;
                    var cost = costCfg.EffectiveFor(moteur);
                    var all = grp.ToList();
                    var blocked = all.Where(c => c.Blocked).ToList();
                    var list = all.Where(c => !c.Blocked).ToList();
                    totBlocked += blocked.Count;

                    // Ordre actuel : par horaire machine manuel (sinon N° dossier).
                    var currentOrder = list
                        .OrderBy(c => c.CurrentMachineTime ?? "99:99", StringComparer.Ordinal)
                        .ThenBy(c => c.NumeroDossier, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Priorité par groupe (papier, format) : un groupe hérite de la priorité
                    // maximale de ses OF, ce qui fait remonter le travail prioritaire tout en
                    // préservant le regroupement (donc les calages économisés).
                    var groupMaxPrio = list
                        .GroupBy(c => c.Papier + "|" + c.Format, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Max(c => c.PriorityScore), StringComparer.OrdinalIgnoreCase);

                    var optimizedOrder = list
                        .OrderByDescending(c => groupMaxPrio[c.Papier + "|" + c.Format])
                        .ThenBy(c => c.Papier, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.Format, StringComparer.OrdinalIgnoreCase)
                        .ThenByDescending(c => c.PriorityScore)
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

                    // Conflit : plus d'un OF urgent le même jour sur la même machine.
                    foreach (var dayGrp in list.Where(c => c.Urgent).GroupBy(c => c.Day))
                    {
                        if (dayGrp.Count() > 1)
                            conflicts.Add(new
                            {
                                moteur = string.IsNullOrWhiteSpace(moteur) ? "(sans moteur)" : moteur,
                                day = dayGrp.Key,
                                count = dayGrp.Count(),
                                dossiers = dayGrp.Select(c => string.IsNullOrWhiteSpace(c.NumeroDossier) ? c.FileName : c.NumeroDossier).ToList()
                            });
                    }

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
                            groupIndex = c.GroupIndex,
                            urgent = c.Urgent,
                            priorityScore = c.PriorityScore,
                            priorityLevel = c.PriorityLevel,
                            priorityReasons = c.PriorityReasons
                        }).ToList(),
                        blocked = blocked.Select(c => new
                        {
                            fileName = c.FileName,
                            numeroDossier = c.NumeroDossier,
                            client = c.Client,
                            papier = c.Papier,
                            reason = c.BlockReason,
                            urgent = c.Urgent,
                            priorityReasons = c.PriorityReasons
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
                    minutesSaved = Math.Max(0, totCurMin - totOptMin),
                    blockedCount = totBlocked,
                    conflictsCount = conflicts.Count,
                    priorityCounts = new
                    {
                        urgent = candidates.Count(c => c.Urgent),
                        vip = candidates.Count(c => c.PriorityReasons.Contains("Client VIP")),
                        retard = candidates.Count(c => c.PriorityReasons.Contains("Retard")),
                        modif = candidates.Count(c => c.PriorityReasons.Contains("Modif. de dernière minute"))
                    },
                    // Machines rendues indisponibles automatiquement par leur statut temps réel.
                    autoUnavailableMachines = autoUnavailable
                        .Where(kv => candidates.Any(c => string.Equals(c.Moteur, kv.Key, StringComparison.OrdinalIgnoreCase)))
                        .Select(kv => new { moteur = kv.Key, statut = kv.Value })
                        .ToList()
                };

                return Results.Json(new { ok = true, summary, machines = machinesOut, conflicts });
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

        // Bascule le drapeau « urgent » d'un OF (stocké dans une collection dédiée,
        // robuste aux enregistrements de fiche qui remplacent le document fabrication).
        app.MapPut("/api/fabrication/urgent", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var fileName = json.TryGetProperty("fileName", out var fn) ? (fn.GetString() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.Json(new { ok = false, error = "fileName requis" });
                bool urgent = json.TryGetProperty("urgent", out var u) && (u.ValueKind == JsonValueKind.True
                    || (u.ValueKind == JsonValueKind.String && bool.TryParse(u.GetString(), out var b) && b));

                var col = MongoDbHelper.GetCollection<BsonDocument>("jobPriority");
                var filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
                var update = Builders<BsonDocument>.Update
                    .Set("fileName", fileName)
                    .Set("urgent", urgent)
                    .Set("urgentSetAt", urgent ? (BsonValue)DateTime.UtcNow : BsonNull.Value)
                    .Set("urgentSetBy", AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "");
                await col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

                return Results.Json(new { ok = true, urgent });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // File d'attente prioritaire : OF triés par score de priorité décroissant.
        app.MapPost("/api/planning/priorities", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var filters = await ReadFiltersAsync(ctx);
                var candidates = LoadCandidates(filters);
                var prioCfg = MongoDbHelper.GetSettings<PriorityConfig>("priorityConfig") ?? new PriorityConfig();
                var vipSet = new HashSet<string>(prioCfg.VipClients ?? new(), StringComparer.OrdinalIgnoreCase);
                var now = DateTime.UtcNow;
                foreach (var c in candidates) ComputePriority(c, prioCfg, vipSet, now, now);

                var items = candidates
                    .OrderByDescending(c => c.PriorityScore)
                    .ThenBy(c => c.DateImpression ?? DateTime.MaxValue)
                    .ThenBy(c => c.NumeroDossier, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new
                    {
                        fileName = c.FileName,
                        numeroDossier = c.NumeroDossier,
                        client = c.Client,
                        moteur = c.Moteur,
                        papier = c.Papier,
                        day = c.Day,
                        urgent = c.Urgent,
                        priorityScore = c.PriorityScore,
                        priorityLevel = c.PriorityLevel,
                        priorityReasons = c.PriorityReasons
                    })
                    .ToList();

                return Results.Json(new { ok = true, items });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private class OptimizeFilters
    {
        public List<string> Moteurs = new();
        public List<string> FileNames = new();
        public DateTime? Start;
        public DateTime? End;
        public List<string> BrokenMachines = new();
        public List<string> OutOfStockPapers = new();
    }

    private static async Task<OptimizeFilters> ReadFiltersAsync(HttpContext ctx)
    {
        var f = new OptimizeFilters();
        try
        {
            var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object)
            {
                List<string> Arr(string name) => json.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array
                    ? el.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                    : new List<string>();

                f.Moteurs = Arr("moteurs");
                f.FileNames = Arr("fileNames");
                f.BrokenMachines = Arr("brokenMachines");
                f.OutOfStockPapers = Arr("outOfStockPapers");
                if (json.TryGetProperty("startDate", out var sEl) && DateTime.TryParse(sEl.GetString(), out var sd))
                    f.Start = DateTime.SpecifyKind(sd.Date, DateTimeKind.Utc);
                if (json.TryGetProperty("endDate", out var eEl) && DateTime.TryParse(eEl.GetString(), out var ed))
                    f.End = DateTime.SpecifyKind(ed.Date.AddDays(1), DateTimeKind.Utc);
            }
        }
        catch { /* corps optionnel */ }
        return f;
    }

    private static List<OfCandidate> LoadCandidates(OptimizeFilters f)
    {
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var conditions = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Exists("dateImpression"),
            Builders<BsonDocument>.Filter.Ne("dateImpression", BsonNull.Value),
            Builders<BsonDocument>.Filter.Ne("excludeFromPlanning", true),
            Builders<BsonDocument>.Filter.Ne("locked", true)
        };
        if (f.Start.HasValue && f.End.HasValue)
        {
            conditions.Add(Builders<BsonDocument>.Filter.Gte("dateImpression", new BsonDateTime(f.Start.Value)));
            conditions.Add(Builders<BsonDocument>.Filter.Lt("dateImpression", new BsonDateTime(f.End.Value)));
        }
        var docs = fabCol.Find(Builders<BsonDocument>.Filter.And(conditions)).ToList();

        var moteurSet = new HashSet<string>(f.Moteurs, StringComparer.OrdinalIgnoreCase);
        var fileSet = new HashSet<string>(f.FileNames, StringComparer.OrdinalIgnoreCase);

        // Charge les OF marqués « urgent » (collection dédiée, robuste aux enregistrements de fiche).
        var urgentSet = LoadUrgentFileNames();

        var result = new List<OfCandidate>();
        foreach (var doc in docs)
        {
            string S(string fld) => doc.Contains(fld) && doc[fld] != BsonNull.Value && doc[fld].IsString ? doc[fld].AsString.Trim() : "";
            int I(string fld)
            {
                try { return doc.Contains(fld) && doc[fld] != BsonNull.Value ? doc[fld].ToInt32() : 0; }
                catch { return 0; }
            }
            DateTime? D(string fld)
            {
                try { return doc.Contains(fld) && doc[fld] != BsonNull.Value ? doc[fld].ToUniversalTime() : (DateTime?)null; }
                catch { return null; }
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

            var dateImpr = D("dateImpression");
            string day = dateImpr?.ToString("yyyy-MM-dd") ?? "";

            string? curTime = null;
            if (doc.Contains("manualPlanningTimes") && doc["manualPlanningTimes"].IsBsonDocument)
            {
                var mpt = doc["manualPlanningTimes"].AsBsonDocument;
                if (mpt.Contains("machineTime") && mpt["machineTime"] != BsonNull.Value && mpt["machineTime"].IsString)
                    curTime = mpt["machineTime"].AsString;
            }

            // Dernière modification : issue de l'historique de la fiche (préservé entre sauvegardes).
            DateTime? lastMod = null;
            bool lastActionModif = false;
            if (doc.Contains("history") && doc["history"].IsBsonArray)
            {
                foreach (var item in doc["history"].AsBsonArray)
                {
                    if (!item.IsBsonDocument) continue;
                    var h = item.AsBsonDocument;
                    DateTime? hd = null;
                    try { if (h.Contains("date") && h["date"] != BsonNull.Value) hd = h["date"].ToUniversalTime(); } catch { }
                    if (hd == null) continue;
                    if (lastMod == null || hd > lastMod)
                    {
                        lastMod = hd;
                        var act = h.Contains("action") && h["action"] != BsonNull.Value && h["action"].IsString ? h["action"].AsString : "";
                        lastActionModif = act.IndexOf("Modif", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
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
                CurrentMachineTime = curTime,
                Urgent = urgentSet.Contains(fileName),
                DateImpression = dateImpr,
                DateReceptionSouhaitee = D("dateReceptionSouhaitee"),
                LastModifiedAt = lastMod,
                LastActionModification = lastActionModif
            });
        }
        return result;
    }

    /// <summary>Noms de fichiers (minuscule) marqués urgent dans la collection jobPriority.</summary>
    private static HashSet<string> LoadUrgentFileNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var col = MongoDbHelper.GetCollection<BsonDocument>("jobPriority");
            var docs = col.Find(Builders<BsonDocument>.Filter.Eq("urgent", true)).ToList();
            foreach (var d in docs)
                if (d.Contains("fileName") && d["fileName"] != BsonNull.Value && d["fileName"].IsString)
                    set.Add(d["fileName"].AsString.Trim());
        }
        catch { }
        return set;
    }

    /// <summary>Moteurs indisponibles (statut « En panne » ou « Maintenance ») → moteur ⇒ statut.</summary>
    private static Dictionary<string, string> LoadUnavailableMachines()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var col = MongoDbHelper.GetCollection<BsonDocument>("machineStatus");
            var docs = col.Find(Builders<BsonDocument>.Filter.In("statut", new[] { "En panne", "Maintenance" })).ToList();
            foreach (var d in docs)
            {
                if (d.Contains("moteur") && d["moteur"] != BsonNull.Value && d["moteur"].IsString
                    && d.Contains("statut") && d["statut"] != BsonNull.Value && d["statut"].IsString)
                    map[d["moteur"].AsString] = d["statut"].AsString;
            }
        }
        catch { }
        return map;
    }

    /// <summary>Calcule le score de priorité, les raisons et le niveau d'un OF.</summary>
    private static void ComputePriority(OfCandidate c, PriorityConfig cfg, HashSet<string> vipSet, DateTime today, DateTime now)
    {
        int score = 0;
        var reasons = new List<string>();

        if (c.Urgent)
        {
            score += cfg.WeightUrgent;
            reasons.Add("Urgent");
        }
        if (!string.IsNullOrWhiteSpace(c.Client) && vipSet.Contains(c.Client))
        {
            score += cfg.WeightVip;
            reasons.Add("Client VIP");
        }
        bool retard = (c.DateImpression.HasValue && c.DateImpression.Value.Date < today.Date)
                   || (c.DateReceptionSouhaitee.HasValue && c.DateReceptionSouhaitee.Value.Date < today.Date);
        if (retard)
        {
            score += cfg.WeightRetard;
            reasons.Add("Retard");
        }
        if (cfg.ModifWindowHours > 0 && c.LastModifiedAt.HasValue && c.LastActionModification
            && (now - c.LastModifiedAt.Value).TotalHours <= cfg.ModifWindowHours)
        {
            score += cfg.WeightModif;
            reasons.Add("Modif. de dernière minute");
        }

        c.PriorityScore = score;
        c.PriorityReasons = reasons;
        c.PriorityLevel = c.Urgent ? "urgent" : (score > 0 ? "elevated" : "normal");
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
