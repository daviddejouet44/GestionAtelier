using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
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
// POST   /api/stock/import           — import CSV ou XML par catégorie
// ======================================================
public static class StockEndpoints
{
    private static IMongoCollection<BsonDocument> Items() => MongoDbHelper.GetCollection<BsonDocument>("stockItems");
    private static IMongoCollection<BsonDocument> Movements() => MongoDbHelper.GetCollection<BsonDocument>("stockMovements");
    private static IMongoCollection<BsonDocument> Cats() => MongoDbHelper.GetCollection<BsonDocument>("stockCategories");

    /// <summary>Retourne les ids des catégories existantes en base (avec seed si vide).</summary>
    private static HashSet<string> GetValidCategoryIds()
    {
        StockCategoriesEndpoints.SeedDefaultCategories();
        var docs = Cats().Find(Builders<BsonDocument>.Filter.Empty).ToList();
        return new HashSet<string>(docs.Select(d => d["_id"].AsString), StringComparer.OrdinalIgnoreCase);
    }

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
        // ── Catégories ───────────────────────────────────────────────────────────
        app.MapStockCategoriesEndpoints();

        // ── Articles ─────────────────────────────────────────────────────────────
        app.MapGet("/api/stock", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var validIds = GetValidCategoryIds();
                var category = (ctx.Request.Query["category"].ToString() ?? "").Trim();
                var filter = Builders<BsonDocument>.Filter.Empty;
                if (!string.IsNullOrWhiteSpace(category) && validIds.Contains(category))
                    filter = Builders<BsonDocument>.Filter.Eq("category", category);

                var docs = Items().Find(filter)
                    .Sort(Builders<BsonDocument>.Sort.Ascending("category").Ascending("name"))
                    .ToList();
                var items = docs.Select(ToDto).ToList();

                // Renvoyer les catégories triées depuis la collection dynamique
                StockCategoriesEndpoints.SeedDefaultCategories();
                var catDocs = Cats().Find(Builders<BsonDocument>.Filter.Empty)
                    .Sort(Builders<BsonDocument>.Sort.Ascending("order").Ascending("_id")).ToList();
                var categories = catDocs.Select(d => new
                {
                    id    = d["_id"].AsString,
                    label = d.Contains("label") && d["label"] != BsonNull.Value ? d["label"].AsString : "",
                    emoji = d.Contains("emoji") && d["emoji"] != BsonNull.Value ? d["emoji"].AsString : "",
                    order = d.Contains("order") && d["order"] != BsonNull.Value ? d["order"].AsInt32 : 0
                }).ToList();

                return Results.Json(new
                {
                    ok = true,
                    categories,
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

                var validIds = GetValidCategoryIds();
                if (string.IsNullOrWhiteSpace(input.Category) || !validIds.Contains(input.Category))
                    return Results.Json(new { ok = false, error = "Catégorie invalide" });

                var doc = new BsonDocument
                {
                    ["name"]         = input.Name.Trim(),
                    ["category"]     = input.Category.Trim().ToLowerInvariant(),
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

                var validIds = GetValidCategoryIds();
                var sets = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow),
                    Builders<BsonDocument>.Update.Set("updatedBy", AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "")
                };
                if (!string.IsNullOrWhiteSpace(input.Name)) sets.Add(Builders<BsonDocument>.Update.Set("name", input.Name.Trim()));
                if (!string.IsNullOrWhiteSpace(input.Category) && validIds.Contains(input.Category))
                    sets.Add(Builders<BsonDocument>.Update.Set("category", input.Category.Trim().ToLowerInvariant()));
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

        // ── Import CSV / XML ──────────────────────────────────────────────────────
        // POST /api/stock/import  (multipart: file + category + mode=overwrite|merge)
        app.MapPost("/api/stock/import", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                if (!ctx.Request.HasFormContentType)
                    return Results.Json(new { ok = false, error = "Requête multipart requise" });

                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                var category = (form["category"].ToString() ?? "").Trim().ToLowerInvariant();
                var mode = (form["mode"].ToString() ?? "merge").Trim().ToLowerInvariant();

                if (file == null || file.Length == 0)
                    return Results.Json(new { ok = false, error = "Fichier requis" });

                var validIds = GetValidCategoryIds();
                if (string.IsNullOrWhiteSpace(category) || !validIds.Contains(category))
                    return Results.Json(new { ok = false, error = "Catégorie invalide" });

                if (mode != "overwrite" && mode != "merge")
                    mode = "merge";

                var fileName = file.FileName ?? "";
                var ext = Path.GetExtension(fileName).ToLowerInvariant();

                // Lire le fichier en mémoire
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                List<StockImportRow> rows;
                if (ext == ".xml")
                    rows = ParseXml(ms);
                else
                    rows = ParseCsv(ms);

                if (rows.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucune ligne valide trouvée dans le fichier" });

                var user = AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "";

                // Mode écraser : supprimer d'abord tous les articles de la catégorie
                if (mode == "overwrite")
                    Items().DeleteMany(Builders<BsonDocument>.Filter.Eq("category", category));

                int added = 0, updated = 0, skipped = 0;
                var errors = new List<string>();

                foreach (var (row, idx) in rows.Select((r, i) => (r, i + 1)))
                {
                    if (string.IsNullOrWhiteSpace(row.Name))
                    {
                        errors.Add($"Ligne {idx} : nom vide, ignorée");
                        skipped++;
                        continue;
                    }
                    try
                    {
                        if (mode == "overwrite")
                        {
                            // Mode écraser : insérer directement
                            var doc = BuildItemDoc(row, category, user);
                            await Items().InsertOneAsync(doc);
                            added++;
                        }
                        else
                        {
                            // Mode fusionner : chercher par nom ou référence
                            var matchFilter = BuildMatchFilter(row, category);
                            var existing = Items().Find(matchFilter).FirstOrDefault();
                            if (existing != null)
                            {
                                var sets = BuildUpdateSets(row, user);
                                await Items().UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", existing["_id"]),
                                    Builders<BsonDocument>.Update.Combine(sets));
                                updated++;
                            }
                            else
                            {
                                var doc = BuildItemDoc(row, category, user);
                                await Items().InsertOneAsync(doc);
                                added++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Ligne {idx} ({row.Name}) : {ex.Message}");
                        skipped++;
                    }
                }

                return Results.Json(new { ok = true, added, updated, skipped, errors });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    // ── Helpers import ──────────────────────────────────────────────────────────

    private record StockImportRow(
        string Name, string Unit, double Quantity, double MinThreshold,
        string Supplier, string Reference, string Note);

    private static FilterDefinition<BsonDocument> BuildMatchFilter(StockImportRow row, string category)
    {
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("category", category)
        };
        if (!string.IsNullOrWhiteSpace(row.Reference))
        {
            filters.Add(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Regex("name", new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(row.Name.Trim()) + "$", "i")),
                Builders<BsonDocument>.Filter.Regex("reference", new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(row.Reference.Trim()) + "$", "i"))
            ));
        }
        else
        {
            filters.Add(Builders<BsonDocument>.Filter.Regex("name", new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(row.Name.Trim()) + "$", "i")));
        }
        return Builders<BsonDocument>.Filter.And(filters);
    }

    private static BsonDocument BuildItemDoc(StockImportRow row, string category, string user) => new BsonDocument
    {
        ["name"]         = row.Name.Trim(),
        ["category"]     = category,
        ["unit"]         = row.Unit,
        ["quantity"]     = Math.Max(0, row.Quantity),
        ["minThreshold"] = Math.Max(0, row.MinThreshold),
        ["supplier"]     = row.Supplier,
        ["reference"]    = row.Reference,
        ["note"]         = row.Note,
        ["updatedAt"]    = DateTime.UtcNow,
        ["updatedBy"]    = user
    };

    private static List<UpdateDefinition<BsonDocument>> BuildUpdateSets(StockImportRow row, string user)
    {
        var sets = new List<UpdateDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow),
            Builders<BsonDocument>.Update.Set("updatedBy", user)
        };
        if (!string.IsNullOrWhiteSpace(row.Name)) sets.Add(Builders<BsonDocument>.Update.Set("name", row.Name.Trim()));
        if (!string.IsNullOrWhiteSpace(row.Unit)) sets.Add(Builders<BsonDocument>.Update.Set("unit", row.Unit));
        if (row.Quantity > 0) sets.Add(Builders<BsonDocument>.Update.Set("quantity", row.Quantity));
        if (row.MinThreshold > 0) sets.Add(Builders<BsonDocument>.Update.Set("minThreshold", row.MinThreshold));
        if (!string.IsNullOrWhiteSpace(row.Supplier)) sets.Add(Builders<BsonDocument>.Update.Set("supplier", row.Supplier));
        if (!string.IsNullOrWhiteSpace(row.Reference)) sets.Add(Builders<BsonDocument>.Update.Set("reference", row.Reference));
        if (!string.IsNullOrWhiteSpace(row.Note)) sets.Add(Builders<BsonDocument>.Update.Set("note", row.Note));
        return sets;
    }

    // ── Parseurs ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse un fichier CSV (séparateur ; ou ,).
    /// Colonnes attendues (insensible à la casse) : nom, quantité/quantite, unité/unite,
    /// seuil, fournisseur, référence/reference, note.
    /// </summary>
    private static List<StockImportRow> ParseCsv(Stream stream)
    {
        var rows = new List<StockImportRow>();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var header = reader.ReadLine();
        if (header == null) return rows;

        char sep = header.Contains(';') ? ';' : ',';
        var cols = header.Split(sep).Select(c => c.Trim().Trim('"').ToLowerInvariant()).ToArray();

        int Idx(params string[] names)
        {
            foreach (var n in names)
            {
                var i = Array.IndexOf(cols, n);
                if (i >= 0) return i;
            }
            return -1;
        }

        int iName = Idx("nom", "name");
        if (iName < 0) iName = 0; // première colonne par défaut
        int iQty  = Idx("quantité", "quantite", "quantity", "qte");
        int iUnit = Idx("unité", "unite", "unit", "unite");
        int iMin  = Idx("seuil", "seuil_alerte", "minthreshold", "threshold");
        int iSup  = Idx("fournisseur", "supplier");
        int iRef  = Idx("référence", "reference", "ref");
        int iNote = Idx("note", "notes", "commentaire");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = SplitCsvLine(line, sep);

            string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim().Trim('"').Trim() : "";
            double GetD(int idx) { var s = Get(idx).Replace(',', '.'); return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0; }

            var name = Get(iName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            rows.Add(new StockImportRow(name, Get(iUnit), GetD(iQty), GetD(iMin), Get(iSup), Get(iRef), Get(iNote)));
        }
        return rows;
    }

    private static string[] SplitCsvLine(string line, char sep)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { inQuote = !inQuote; }
            else if (c == sep && !inQuote) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    /// <summary>
    /// Parse un fichier XML.
    /// Format attendu :
    ///   &lt;articles&gt;
    ///     &lt;article nom="..." quantite="..." unite="..." seuil="..." fournisseur="..." reference="..." note="..." /&gt;
    ///   &lt;/articles&gt;
    /// Les valeurs peuvent aussi être des éléments enfants.
    /// </summary>
    private static List<StockImportRow> ParseXml(Stream stream)
    {
        var rows = new List<StockImportRow>();
        try
        {
            var doc = XDocument.Load(stream);
            // Chercher tous les éléments qui ressemblent à des articles
            var items = doc.Descendants()
                .Where(e => e.Name.LocalName.Equals("article", StringComparison.OrdinalIgnoreCase)
                         || e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)
                         || e.Name.LocalName.Equals("produit", StringComparison.OrdinalIgnoreCase));

            string Attr(XElement e, params string[] names)
            {
                foreach (var n in names)
                {
                    var a = e.Attribute(n)?.Value ?? e.Element(n)?.Value
                         ?? e.Attribute(n.ToLower())?.Value ?? e.Element(n.ToLower())?.Value
                         ?? e.Attribute(n.ToUpper())?.Value;
                    if (a != null) return a.Trim();
                }
                return "";
            }
            double AttrD(XElement e, params string[] names)
            {
                var s = Attr(e, names).Replace(',', '.');
                return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

            foreach (var item in items)
            {
                var name = Attr(item, "nom", "name", "Nom", "libellé", "libelle", "designation");
                if (string.IsNullOrWhiteSpace(name)) continue;
                rows.Add(new StockImportRow(
                    name,
                    Attr(item, "unite", "unité", "unit"),
                    AttrD(item, "quantite", "quantité", "quantity", "qte"),
                    AttrD(item, "seuil", "minthreshold", "threshold"),
                    Attr(item, "fournisseur", "supplier"),
                    Attr(item, "reference", "référence", "ref"),
                    Attr(item, "note", "notes", "commentaire")
                ));
            }
        }
        catch { /* retourne liste vide si XML invalide */ }
        return rows;
    }
}
