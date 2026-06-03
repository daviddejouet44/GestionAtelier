using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Fabrication;

public static class FabricationExportEndpoints
{
    public static void MapFabricationExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/fabrication/export", (string? fileName, string? format, HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3)
                    return Results.Json(new { ok = false, error = "Token invalide" });

                var users = BackendUtils.LoadUsers();
                if (!users.Any(u => u.Id == parts[0]))
                    return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.Json(new { ok = false, error = "fileName requis" });

                var col = MongoDbHelper.GetFabricationsCollection();
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
                if (doc == null)
                    doc = col.Find(Builders<BsonDocument>.Filter.Regex("fileName",
                        new MongoDB.Bson.BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(fileName) + "$", "i")))
                        .SortByDescending(x => x["_id"]).FirstOrDefault();
                if (doc == null)
                    return Results.Json(new { ok = false, error = "Fiche introuvable" });

                var exportFormat = (format ?? "xml").Trim().ToLowerInvariant();
                if (exportFormat != "xml" && exportFormat != "csv")
                    return Results.Json(new { ok = false, error = "Format non supporté (xml/csv)" });

                var safeName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "fiche";

                if (exportFormat == "xml")
                {
                    var root = new XElement("Fiche");
                    foreach (var el in doc.Elements)
                    {
                        var value = BsonToExportString(el.Value);
                        root.Add(new XElement(el.Name, value));
                    }

                    var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
                    var bytes = Encoding.UTF8.GetBytes(xdoc.ToString(SaveOptions.None));
                    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{safeName}.xml\"";
                    return Results.Bytes(bytes, "application/xml; charset=utf-8");
                }

                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("Champ,Valeur");
                foreach (var el in doc.Elements)
                {
                    sb.AppendLine($"{CsvEsc(el.Name)},{CsvEsc(BsonToExportString(el.Value))}");
                }
                var csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
                ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{safeName}.csv\"";
                return Results.Bytes(csvBytes, "text/csv; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });
    }

    private static string CsvEsc(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    private static string BsonToExportString(BsonValue value)
    {
        if (value == null || value.IsBsonNull) return "";
        return value.BsonType switch
        {
            BsonType.Array => string.Join(", ", value.AsBsonArray.Select(BsonToExportString)),
            BsonType.Document => value.AsBsonDocument.ToJson(),
            BsonType.DateTime => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            BsonType.Boolean => value.AsBoolean ? "true" : "false",
            _ => value.ToString() ?? ""
        };
    }
}
