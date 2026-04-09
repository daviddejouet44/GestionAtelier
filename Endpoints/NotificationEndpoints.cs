using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

public static class NotificationEndpointsExtensions
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
// ======================================================
app.MapGet("/api/notifications", (string? login) =>
{
    if (string.IsNullOrWhiteSpace(login)) return Results.Json(new object[0]);
    var col = MongoDbHelper.GetCollection<BsonDocument>("notifications");
    var filter = Builders<BsonDocument>.Filter.And(
        Builders<BsonDocument>.Filter.Eq("recipientLogin", login),
        Builders<BsonDocument>.Filter.Eq("read", false)
    );
    var docs = col.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("timestamp")).Limit(20).ToList();
    return Results.Json(docs.Select(d => new {
        id = d["_id"].ToString(),
        type = d.Contains("type") ? d["type"].AsString : "general",
        message = d.Contains("message") ? d["message"].AsString : "",
        fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
        numeroDossier = d.Contains("numeroDossier") ? d["numeroDossier"].AsString : "",
        timestamp = d.Contains("timestamp") ? d["timestamp"].ToUniversalTime().ToString("o") : "",
        read = d.Contains("read") && d["read"].AsBoolean,
        prismaLog = d.Contains("prismaLog") ? d["prismaLog"].AsString : ""
    }));
});

app.MapPut("/api/notifications/read", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var login = json.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("notifications");
    var filter = Builders<BsonDocument>.Filter.Eq("recipientLogin", login);
    col.UpdateMany(filter, Builders<BsonDocument>.Update.Set("read", true));
    return Results.Json(new { ok = true });
});

// ======================================================
// JOBS — Archiver (déplacer vers le dossier de production archive/)
// ======================================================

    }
}
