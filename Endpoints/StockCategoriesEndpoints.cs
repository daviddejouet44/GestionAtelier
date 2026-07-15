using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// Catégories de stock dynamiques
// GET    /api/stock/categories             — liste (seed si vide)
// POST   /api/stock/categories             — créer
// PUT    /api/stock/categories/{id}        — renommer / modifier
// DELETE /api/stock/categories/{id}        — supprimer (admin, bloqué si articles présents)
// ======================================================
public static class StockCategoriesEndpoints
{
    private static IMongoCollection<BsonDocument> Cats()  => MongoDbHelper.GetCollection<BsonDocument>("stockCategories");
    private static IMongoCollection<BsonDocument> Items() => MongoDbHelper.GetCollection<BsonDocument>("stockItems");

    /// <summary>Graine les 5 catégories par défaut si la collection est vide.</summary>
    public static void SeedDefaultCategories()
    {
        try
        {
            var col = Cats();
            if (col.CountDocuments(Builders<BsonDocument>.Filter.Empty) > 0) return;
            foreach (var (id, label, emoji, order) in StockCategoryDefaults.All)
            {
                col.InsertOne(new BsonDocument
                {
                    ["_id"]   = id,
                    ["label"] = label,
                    ["emoji"] = emoji,
                    ["order"] = order
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] StockCategories seed failed: {ex.Message}");
        }
    }

    private static object ToDto(BsonDocument d) => new
    {
        id    = d["_id"].AsString,
        label = d.Contains("label") && d["label"] != BsonNull.Value ? d["label"].AsString : "",
        emoji = d.Contains("emoji") && d["emoji"] != BsonNull.Value ? d["emoji"].AsString : "",
        order = d.Contains("order") && d["order"] != BsonNull.Value ? d["order"].AsInt32 : 0
    };

    public static void MapStockCategoriesEndpoints(this WebApplication app)
    {
        // GET /api/stock/categories
        app.MapGet("/api/stock/categories", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                SeedDefaultCategories();
                var docs = Cats().Find(Builders<BsonDocument>.Filter.Empty)
                    .Sort(Builders<BsonDocument>.Sort.Ascending("order").Ascending("_id"))
                    .ToList();
                return Results.Json(new { ok = true, categories = docs.Select(ToDto).ToList() });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // POST /api/stock/categories
        app.MapPost("/api/stock/categories", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var input = await ctx.Request.ReadFromJsonAsync<StockCategoryInput>();
                if (input == null || string.IsNullOrWhiteSpace(input.Label))
                    return Results.Json(new { ok = false, error = "Libellé requis" });

                // Générer un id slug à partir du libellé
                var id = ToSlug(input.Label.Trim());
                if (Cats().Find(Builders<BsonDocument>.Filter.Eq("_id", id)).Any())
                    return Results.Json(new { ok = false, error = "Une catégorie avec cet identifiant existe déjà" });

                var maxOrder = Cats().Find(Builders<BsonDocument>.Filter.Empty).ToList()
                    .Select(d => d.Contains("order") ? d["order"].AsInt32 : 0)
                    .DefaultIfEmpty(0).Max();

                var doc = new BsonDocument
                {
                    ["_id"]   = id,
                    ["label"] = input.Label.Trim(),
                    ["emoji"] = input.Emoji ?? "",
                    ["order"] = input.Order.HasValue ? input.Order.Value : maxOrder + 1
                };
                await Cats().InsertOneAsync(doc);
                return Results.Json(new { ok = true, id });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // PUT /api/stock/categories/{id}
        app.MapPut("/api/stock/categories/{id}", async (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var input = await ctx.Request.ReadFromJsonAsync<StockCategoryInput>();
                if (input == null) return Results.Json(new { ok = false, error = "Corps requis" });

                var sets = new List<UpdateDefinition<BsonDocument>>();
                if (!string.IsNullOrWhiteSpace(input.Label))
                    sets.Add(Builders<BsonDocument>.Update.Set("label", input.Label.Trim()));
                if (input.Emoji != null)
                    sets.Add(Builders<BsonDocument>.Update.Set("emoji", input.Emoji));
                if (input.Order.HasValue)
                    sets.Add(Builders<BsonDocument>.Update.Set("order", input.Order.Value));

                if (sets.Count == 0) return Results.Json(new { ok = false, error = "Aucun champ à modifier" });

                var res = await Cats().UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id),
                    Builders<BsonDocument>.Update.Combine(sets));
                if (res.MatchedCount == 0)
                    return Results.Json(new { ok = false, error = "Catégorie introuvable" });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // DELETE /api/stock/categories/{id}  (admin uniquement)
        app.MapDelete("/api/stock/categories/{id}", (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                // Bloquer si des articles utilisent encore cette catégorie
                var count = Items().CountDocuments(Builders<BsonDocument>.Filter.Eq("category", id));
                if (count > 0)
                    return Results.Json(new { ok = false, error = $"Impossible de supprimer : {count} article(s) utilisent cette catégorie. Déplacez-les d'abord." });

                var res = Cats().DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", id));
                if (res.DeletedCount == 0)
                    return Results.Json(new { ok = false, error = "Catégorie introuvable" });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    private static string ToSlug(string label)
    {
        var s = label.ToLowerInvariant()
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
            .Replace("à", "a").Replace("â", "a").Replace("ä", "a")
            .Replace("ô", "o").Replace("ö", "o")
            .Replace("ù", "u").Replace("û", "u").Replace("ü", "u")
            .Replace("î", "i").Replace("ï", "i")
            .Replace("ç", "c");
        var result = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) result.Append(c);
            else if (c == ' ' || c == '-' || c == '_') result.Append('_');
        }
        return result.ToString().Trim('_');
    }
}
