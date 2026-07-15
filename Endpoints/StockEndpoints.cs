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
// Gestion des stocks (point 7)
// GET    /api/stock                  — liste des articles (+ statut ok/bas/rupture)
// GET    /api/stock/alerts           — articles en rupture / bas (alertes)
// POST   /api/stock                  — créer un article
// PUT    /api/stock/{id}             — modifier les métadonnées d'un article
// DELETE /api/stock/{id}             — supprimer (admin)
// POST   /api/stock/{id}/movement    — entrée / sortie / ajustement
// GET    /api/stock/{id}/movements   — historique des mouvements
// ======================================================
public static class StockEndpoints
{
    private static IMongoCollection<BsonDocument> Items() => MongoDbHelper.GetCollection<BsonDocument>("stockItems");
    private static IMongoCollection<BsonDocument> Movements() => MongoDbHelper.GetCollection<BsonDocument>("stockMovements");

    private static object ToDto(BsonDocument d)
    {
        double qty = 0, min = 0;
        try { qty = d.Contains("quantity") && d["quantity"] != BsonNull.Value ? d["quantity"].ToDouble() : 0; } catch { }
        try { min = d.Contains("minThreshold") && d["minThreshold"] != BsonNull.Value ? d["minThreshold"].ToDouble() : 0; } catch { }
        string S(string f) => d.Contains(f) && d[f] != BsonNull.Value && d[f].IsString ? d[f].AsString : "";
        DateTime? T(string f) { try { return d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToUniversalTime() : (DateTime?)null; } catch { return null; } }
        return new
        {
            id = d["_id"].AsObjectId.ToString(),
            name = S("name"),
            category = S("category"),
            unit = S("unit"),
            quantity = qty,
            minThreshold = min,
            supplier = S("supplier"),
            reference = S("reference"),
            note = S("note"),
            status = StockStatus.Compute(qty, min),
            updatedAt = T("updatedAt"),
            updatedBy = S("updatedBy")
        };
    }

    private static bool TryId(string id, out ObjectId oid) => ObjectId.TryParse(id, out oid);

    private static string StatusOf(BsonDocument d)
    {
        double qty = 0, min = 0;
        try { qty = d.Contains("quantity") && d["quantity"] != BsonNull.Value ? d["quantity"].ToDouble() : 0; } catch { }
        try { min = d.Contains("minThreshold") && d["minThreshold"] != BsonNull.Value ? d["minThreshold"].ToDouble() : 0; } catch { }
        return StockStatus.Compute(qty, min);
    }

    public static void MapStockEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stock", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var category = (ctx.Request.Query["category"].ToString() ?? "").Trim();
                var filter = Builders<BsonDocument>.Filter.Empty;
                if (StockCategories.IsValid(category))
                    filter = Builders<BsonDocument>.Filter.Eq("category", StockCategories.Canonical(category));

                var docs = Items().Find(filter)
                    .Sort(Builders<BsonDocument>.Sort.Ascending("category").Ascending("name"))
                    .ToList();
                var items = docs.Select(ToDto).ToList();

                return Results.Json(new
                {
                    ok = true,
                    categories = StockCategories.All,
                    items,
                    alertCount = docs.Count(d => StatusOf(d) != "ok")
                });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapGet("/api/stock/alerts", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var docs = Items().Find(Builders<BsonDocument>.Filter.Empty).ToList();
                var alerts = docs.Where(d => StatusOf(d) != "ok").Select(ToDto).ToList();
                return Results.Json(new { ok = true, alerts, count = alerts.Count });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPost("/api/stock", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var input = await ctx.Request.ReadFromJsonAsync<StockItemInput>();
                if (input == null || string.IsNullOrWhiteSpace(input.Name))
                    return Results.Json(new { ok = false, error = "Nom requis" });
                if (!StockCategories.IsValid(input.Category))
                    return Results.Json(new { ok = false, error = "Catégorie invalide" });

                var doc = new BsonDocument
                {
                    ["name"]         = input.Name.Trim(),
                    ["category"]     = StockCategories.Canonical(input.Category!),
                    ["unit"]         = input.Unit ?? "",
                    ["quantity"]     = Math.Max(0, input.Quantity ?? 0),
                    ["minThreshold"] = Math.Max(0, input.MinThreshold ?? 0),
                    ["supplier"]     = input.Supplier ?? "",
                    ["reference"]    = input.Reference ?? "",
                    ["note"]         = input.Note ?? "",
                    ["updatedAt"]    = DateTime.UtcNow,
                    ["updatedBy"]    = AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? ""
                };
                await Items().InsertOneAsync(doc);
                return Results.Json(new { ok = true, id = doc["_id"].AsObjectId.ToString() });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/stock/{id}", async (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);
                if (!TryId(id, out var oid))
                    return Results.Json(new { ok = false, error = "Identifiant invalide" });

                var input = await ctx.Request.ReadFromJsonAsync<StockItemInput>();
                if (input == null) return Results.Json(new { ok = false, error = "Corps requis" });

                var sets = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow),
                    Builders<BsonDocument>.Update.Set("updatedBy", AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "")
                };
                if (!string.IsNullOrWhiteSpace(input.Name)) sets.Add(Builders<BsonDocument>.Update.Set("name", input.Name.Trim()));
                if (StockCategories.IsValid(input.Category)) sets.Add(Builders<BsonDocument>.Update.Set("category", StockCategories.Canonical(input.Category!)));
                if (input.Unit != null) sets.Add(Builders<BsonDocument>.Update.Set("unit", input.Unit));
                if (input.MinThreshold.HasValue) sets.Add(Builders<BsonDocument>.Update.Set("minThreshold", Math.Max(0, input.MinThreshold.Value)));
                if (input.Supplier != null) sets.Add(Builders<BsonDocument>.Update.Set("supplier", input.Supplier));
                if (input.Reference != null) sets.Add(Builders<BsonDocument>.Update.Set("reference", input.Reference));
                if (input.Note != null) sets.Add(Builders<BsonDocument>.Update.Set("note", input.Note));

                var res = await Items().UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", oid),
                    Builders<BsonDocument>.Update.Combine(sets));
                if (res.MatchedCount == 0) return Results.Json(new { ok = false, error = "Article introuvable" });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapDelete("/api/stock/{id}", (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });
                if (!TryId(id, out var oid))
                    return Results.Json(new { ok = false, error = "Identifiant invalide" });

                Items().DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", oid));
                Movements().DeleteMany(Builders<BsonDocument>.Filter.Eq("itemId", oid.ToString()));
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPost("/api/stock/{id}/movement", async (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);
                if (!TryId(id, out var oid))
                    return Results.Json(new { ok = false, error = "Identifiant invalide" });

                var input = await ctx.Request.ReadFromJsonAsync<StockMovementInput>();
                var type = (input?.Type ?? "").Trim().ToLowerInvariant();
                if (type != "entree" && type != "sortie" && type != "ajustement")
                    return Results.Json(new { ok = false, error = "Type invalide (entree|sortie|ajustement)" });
                var qty = input?.Quantity ?? 0;
                if (qty < 0) return Results.Json(new { ok = false, error = "Quantité invalide" });

                var doc = Items().Find(Builders<BsonDocument>.Filter.Eq("_id", oid)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Article introuvable" });

                double current = 0;
                try { current = doc.Contains("quantity") && doc["quantity"] != BsonNull.Value ? doc["quantity"].ToDouble() : 0; } catch { }

                double newQty; double delta;
                switch (type)
                {
                    case "entree":     newQty = current + qty; delta = qty; break;
                    case "sortie":     newQty = Math.Max(0, current - qty); delta = newQty - current; break;
                    default:           newQty = qty; delta = qty - current; break; // ajustement (absolu)
                }

                await Items().UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", oid),
                    Builders<BsonDocument>.Update
                        .Set("quantity", newQty)
                        .Set("updatedAt", DateTime.UtcNow)
                        .Set("updatedBy", AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? ""));

                await Movements().InsertOneAsync(new BsonDocument
                {
                    ["itemId"]    = oid.ToString(),
                    ["itemName"]  = doc.Contains("name") && doc["name"] != BsonNull.Value ? doc["name"] : "",
                    ["category"]  = doc.Contains("category") && doc["category"] != BsonNull.Value ? doc["category"] : "",
                    ["type"]      = type,
                    ["delta"]     = delta,
                    ["quantityAfter"] = newQty,
                    ["reason"]    = input?.Reason ?? "",
                    ["at"]        = DateTime.UtcNow,
                    ["by"]        = AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? ""
                });

                double min = 0;
                try { min = doc.Contains("minThreshold") && doc["minThreshold"] != BsonNull.Value ? doc["minThreshold"].ToDouble() : 0; } catch { }
                return Results.Json(new { ok = true, quantity = newQty, status = StockStatus.Compute(newQty, min) });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapGet("/api/stock/{id}/movements", (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);
                if (!TryId(id, out var oid))
                    return Results.Json(new { ok = false, error = "Identifiant invalide" });

                var docs = Movements().Find(Builders<BsonDocument>.Filter.Eq("itemId", oid.ToString()))
                    .Sort(Builders<BsonDocument>.Sort.Descending("at")).Limit(50).ToList();
                var list = docs.Select(d => new
                {
                    type = d.Contains("type") ? d["type"].AsString : "",
                    delta = d.Contains("delta") && d["delta"] != BsonNull.Value ? d["delta"].ToDouble() : 0,
                    quantityAfter = d.Contains("quantityAfter") && d["quantityAfter"] != BsonNull.Value ? d["quantityAfter"].ToDouble() : 0,
                    reason = d.Contains("reason") && d["reason"] != BsonNull.Value ? d["reason"].AsString : "",
                    at = d.Contains("at") && d["at"] != BsonNull.Value ? d["at"].ToUniversalTime() : (DateTime?)null,
                    by = d.Contains("by") && d["by"] != BsonNull.Value ? d["by"].AsString : ""
                }).ToList();
                return Results.Json(new { ok = true, movements = list });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
