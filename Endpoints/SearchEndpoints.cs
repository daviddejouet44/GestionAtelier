using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// Recherche globale ultra-rapide (point 6)
// GET /api/search?q=<texte>&limit=<n>
// Cherche dans : client, n° OF, référence, nom PDF, opérateur, papier, machine, date.
// ======================================================
public static class SearchEndpoints
{
    // Champs texte interrogés (regex insensible à la casse).
    private static readonly string[] TextFields =
    {
        "numeroDossier", "client", "numeroAffaire", "fileName", "operateur",
        "moteurImpression", "machine", "media1", "media2", "media3", "media4",
        "mediaCouverture", "papier", "typeTravail", "format", "formatFeuille"
    };

    // Champs date testés lorsqu'un terme ressemble à une date.
    private static readonly string[] DateFields =
    {
        "dateImpression", "dateReception", "dateReceptionSouhaitee",
        "dateEnvoi", "dateDepart", "dateLivraison"
    };

    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var q = (ctx.Request.Query["q"].ToString() ?? "").Trim();
                if (q.Length < 2)
                    return Results.Json(new { ok = true, results = new object[0] });

                int limit = 20;
                if (int.TryParse(ctx.Request.Query["limit"], out var l)) limit = Math.Clamp(l, 1, 50);

                // Chaque terme doit correspondre (AND) ; un terme peut matcher n'importe quel champ (OR).
                var tokens = q.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var andConditions = new List<FilterDefinition<BsonDocument>>();

                foreach (var token in tokens)
                {
                    var orForToken = new List<FilterDefinition<BsonDocument>>();
                    var rx = new BsonRegularExpression(Regex.Escape(token), "i");
                    foreach (var f in TextFields)
                        orForToken.Add(Builders<BsonDocument>.Filter.Regex(f, rx));

                    // Terme ressemblant à une date → recherche sur les champs date (jour exact).
                    if (TryParseDay(token, out var dayStart))
                    {
                        var dayEnd = dayStart.AddDays(1);
                        foreach (var df in DateFields)
                            orForToken.Add(Builders<BsonDocument>.Filter.And(
                                Builders<BsonDocument>.Filter.Gte(df, new BsonDateTime(dayStart)),
                                Builders<BsonDocument>.Filter.Lt(df, new BsonDateTime(dayEnd))));
                    }

                    andConditions.Add(Builders<BsonDocument>.Filter.Or(orForToken));
                }

                var filter = andConditions.Count > 0
                    ? Builders<BsonDocument>.Filter.And(andConditions)
                    : Builders<BsonDocument>.Filter.Empty;

                var fabCol = MongoDbHelper.GetFabricationsCollection();
                var docs = fabCol.Find(filter)
                    .Sort(Builders<BsonDocument>.Sort.Descending("_id"))
                    .Limit(limit)
                    .ToList();

                string S(BsonDocument d, string f) =>
                    d.Contains(f) && d[f] != BsonNull.Value && d[f].IsString ? d[f].AsString : "";
                string D(BsonDocument d, string f)
                {
                    try { return d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToUniversalTime().ToString("yyyy-MM-dd") : ""; }
                    catch { return ""; }
                }

                var results = docs.Select(d => new
                {
                    fileName = S(d, "fileName"),
                    fullPath = S(d, "fullPath"),
                    numeroDossier = S(d, "numeroDossier"),
                    client = S(d, "client"),
                    reference = S(d, "numeroAffaire"),
                    operateur = S(d, "operateur"),
                    moteur = S(d, "moteurImpression"),
                    papier = string.IsNullOrWhiteSpace(S(d, "media1")) ? S(d, "papier") : S(d, "media1"),
                    typeTravail = S(d, "typeTravail"),
                    dateImpression = D(d, "dateImpression"),
                    dateReceptionSouhaitee = D(d, "dateReceptionSouhaitee")
                }).ToList();

                return Results.Json(new { ok = true, count = results.Count, results });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    /// <summary>Reconnaît yyyy-MM-dd, dd/MM/yyyy, dd-MM-yyyy (jour UTC).</summary>
    private static bool TryParseDay(string token, out DateTime dayStart)
    {
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d-M-yyyy" };
        if (DateTime.TryParseExact(token, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            dayStart = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
            return true;
        }
        dayStart = default;
        return false;
    }
}
