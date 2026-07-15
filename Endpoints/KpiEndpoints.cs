using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// KPI de production (point 9)
// GET /api/kpi?from=YYYY-MM-DD&to=YYYY-MM-DD
// Agrège les fiches (temps, feuilles, OF) + le journal d'événements machine
// (occupation, arrêts, causes, disponibilité).
// ======================================================
public static class KpiEndpoints
{
    private const string DOWN_PANNE = "En panne";
    private const string DOWN_MAINT = "Maintenance";
    private const string RUN = "En impression";

    public static void MapKpiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/kpi", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var now = DateTime.UtcNow;
                DateTime from = ParseDay(ctx.Request.Query["from"], now.Date.AddDays(-29));
                DateTime toExclusive = ParseDay(ctx.Request.Query["to"], now.Date).AddDays(1);
                if (toExclusive <= from) toExclusive = from.AddDays(1);
                var rangeEnd = toExclusive < now ? toExclusive : now; // borne au présent pour les durées

                // ── Fiches de fabrication de la période (par dateImpression) ──────────
                var fabCol = MongoDbHelper.GetFabricationsCollection();
                // KPI de production : on compte tout OF ayant une date d'impression dans la
                // période (indépendamment de l'exclusion du planning), pour refléter la production réelle.
                var fabFilter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Exists("dateImpression"),
                    Builders<BsonDocument>.Filter.Ne("dateImpression", BsonNull.Value),
                    Builders<BsonDocument>.Filter.Gte("dateImpression", new BsonDateTime(from)),
                    Builders<BsonDocument>.Filter.Lt("dateImpression", new BsonDateTime(toExclusive))
                );
                var fabs = fabCol.Find(fabFilter).ToList();

                int ofCount = fabs.Count;
                long totalFeuilles = 0, totalQuantite = 0;
                int totalTemps = 0;
                var byMachine = new Dictionary<string, (int of, long feuilles, int temps)>(StringComparer.OrdinalIgnoreCase);
                var byOperateur = new Dictionary<string, (int of, int temps)>(StringComparer.OrdinalIgnoreCase);
                var byDay = new Dictionary<string, (int of, long feuilles, int temps)>();

                foreach (var d in fabs)
                {
                    long feuilles = LongOf(d, "nombreFeuilles");
                    long qte = LongOf(d, "quantite");
                    int temps = IntOf(d, "tempsProduitMinutes");
                    totalFeuilles += feuilles; totalQuantite += qte; totalTemps += temps;

                    var moteur = StrOf(d, "moteurImpression"); if (moteur == "") moteur = "(sans moteur)";
                    var m = byMachine.GetValueOrDefault(moteur); byMachine[moteur] = (m.of + 1, m.feuilles + feuilles, m.temps + temps);

                    var op = StrOf(d, "operateur"); if (op == "") op = "(non assigné)";
                    var o = byOperateur.GetValueOrDefault(op); byOperateur[op] = (o.of + 1, o.temps + temps);

                    string day = ""; try { day = d["dateImpression"].ToUniversalTime().ToString("yyyy-MM-dd"); } catch { }
                    if (day != "") { var dd = byDay.GetValueOrDefault(day); byDay[day] = (dd.of + 1, dd.feuilles + feuilles, dd.temps + temps); }
                }

                // ── Occupation / arrêts depuis le journal d'événements machine ────────
                var evCol = MongoDbHelper.GetCollection<BsonDocument>("machineEvents");
                var allEvents = evCol.Find(Builders<BsonDocument>.Filter.Empty)
                    .Sort(Builders<BsonDocument>.Sort.Ascending("at")).ToList();
                var eventsByMachine = allEvents.Where(e => e.Contains("moteur") && e["moteur"].IsString)
                    .GroupBy(e => e["moteur"].AsString, StringComparer.OrdinalIgnoreCase);

                var machineOcc = new List<object>();
                var causeTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double totOpenMin = 0, totDownMin = 0, totRunMin = 0;

                foreach (var grp in eventsByMachine)
                {
                    var evts = grp.OrderBy(e => EvAt(e)).ToList();
                    double openMin = (rangeEnd - from).TotalMinutes;
                    if (openMin <= 0) continue;

                    // Statut au début de la période = dernier événement avant "from".
                    string curStatut = "Disponible", curNote = "";
                    foreach (var e in evts) { if (EvAt(e) <= from) { curStatut = StrOf(e, "statut"); curNote = StrOf(e, "note"); } }

                    double runMin = 0, downMin = 0;
                    var localCause = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    DateTime cursor = from;
                    void AddInterval(string statut, string note, DateTime end)
                    {
                        var mins = (end - cursor).TotalMinutes;
                        if (mins <= 0) return;
                        if (string.Equals(statut, RUN, StringComparison.OrdinalIgnoreCase)) runMin += mins;
                        if (string.Equals(statut, DOWN_PANNE, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(statut, DOWN_MAINT, StringComparison.OrdinalIgnoreCase))
                        {
                            downMin += mins;
                            var cause = statut + (string.IsNullOrWhiteSpace(note) ? "" : " — " + note);
                            localCause[cause] = localCause.GetValueOrDefault(cause) + mins;
                            causeTotals[cause] = causeTotals.GetValueOrDefault(cause) + mins;
                        }
                    }
                    foreach (var e in evts)
                    {
                        var at = EvAt(e);
                        if (at <= from || at >= rangeEnd) continue;
                        AddInterval(curStatut, curNote, at);
                        cursor = at; curStatut = StrOf(e, "statut"); curNote = StrOf(e, "note");
                    }
                    AddInterval(curStatut, curNote, rangeEnd);

                    totOpenMin += openMin; totDownMin += downMin; totRunMin += runMin;
                    machineOcc.Add(new
                    {
                        moteur = grp.Key,
                        occupationPct = Pct(runMin, openMin),
                        disponibilitePct = Pct(openMin - downMin, openMin),
                        runMinutes = Math.Round(runMin),
                        downMinutes = Math.Round(downMin),
                        causes = localCause.OrderByDescending(kv => kv.Value)
                            .Select(kv => new { cause = kv.Key, minutes = Math.Round(kv.Value) }).ToList()
                    });
                }

                // ── BAT / PDF refusés dans la période ─────────────────────────────────
                int batRefuses = 0;
                try
                {
                    var batCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
                    batRefuses = (int)batCol.CountDocuments(Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq("status", "rejected"),
                        Builders<BsonDocument>.Filter.Gte("rejectedAt", new BsonDateTime(from)),
                        Builders<BsonDocument>.Filter.Lt("rejectedAt", new BsonDateTime(toExclusive))));
                }
                catch { }

                var summary = new
                {
                    from = from.ToString("yyyy-MM-dd"),
                    to = toExclusive.AddDays(-1).ToString("yyyy-MM-dd"),
                    ofCount,
                    totalFeuilles,
                    totalQuantite,
                    totalTempsMinutes = totalTemps,
                    avgTempsMinutes = ofCount > 0 ? Math.Round((double)totalTemps / ofCount, 1) : 0,
                    tempsPerduMinutes = Math.Round(totDownMin),
                    occupationPct = Pct(totRunMin, totOpenMin),
                    disponibilitePct = Pct(totOpenMin - totDownMin, totOpenMin),
                    batRefuses
                };

                return Results.Json(new
                {
                    ok = true,
                    summary,
                    byMachine = byMachine.OrderByDescending(kv => kv.Value.feuilles)
                        .Select(kv => new { moteur = kv.Key, ofCount = kv.Value.of, feuilles = kv.Value.feuilles, tempsMinutes = kv.Value.temps }).ToList(),
                    byOperateur = byOperateur.OrderByDescending(kv => kv.Value.temps)
                        .Select(kv => new { operateur = kv.Key, ofCount = kv.Value.of, tempsMinutes = kv.Value.temps }).ToList(),
                    byDay = byDay.OrderBy(kv => kv.Key)
                        .Select(kv => new { day = kv.Key, ofCount = kv.Value.of, feuilles = kv.Value.feuilles, tempsMinutes = kv.Value.temps }).ToList(),
                    machineOccupation = machineOcc,
                    causesArret = causeTotals.OrderByDescending(kv => kv.Value)
                        .Select(kv => new { cause = kv.Key, minutes = Math.Round(kv.Value) }).ToList()
                });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    private static DateTime ParseDay(string? s, DateTime fallback)
    {
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
        return DateTime.SpecifyKind(fallback.Date, DateTimeKind.Utc);
    }

    private static double Pct(double part, double whole) => whole > 0 ? Math.Round(part / whole * 100, 1) : 0;

    private static DateTime EvAt(BsonDocument e)
    {
        try { return e.Contains("at") && e["at"] != BsonNull.Value ? e["at"].ToUniversalTime() : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }

    private static string StrOf(BsonDocument d, string f) =>
        d.Contains(f) && d[f] != BsonNull.Value && d[f].IsString ? d[f].AsString.Trim() : "";
    private static int IntOf(BsonDocument d, string f)
    { try { return d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToInt32() : 0; } catch { return 0; } }
    private static long LongOf(BsonDocument d, string f)
    { try { return d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToInt64() : 0; } catch { return 0; } }
}
