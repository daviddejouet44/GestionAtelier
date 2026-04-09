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

public static class RecycleEndpointsExtensions
{
    public static void MapRecycleEndpoints(this WebApplication app, string recyclePath, int recycleDays)
    {
// ======================================================
// CORBEILLE — API
// ======================================================

app.MapGet("/api/recycle/list", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var list = Directory.GetFiles(recyclePath)
            .Select(full => new {
                fullPath = full,
                fileName = Path.GetFileName(full),
                deletedAt = File.GetCreationTime(full)
            })
            .OrderByDescending(x => x.deletedAt)
            .ToList();

        return Results.Json(list);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/recycle/restore", (string fullPath, string destinationFolder) =>
{
    try
    {
        var src = Path.GetFullPath(fullPath);
        if (!File.Exists(src))
            return Results.Json(new { ok = false, error = "Fichier introuvable dans la corbeille." });

        var root = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, destinationFolder);
        Directory.CreateDirectory(destDir);

        var dest = Path.Combine(destDir, Path.GetFileName(src));
        File.Move(src, dest);

        return Results.Json(new { ok = true, restoredTo = dest });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/recycle/purge", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var count = 0;
        foreach (var f in Directory.GetFiles(recyclePath))
        {
            var age = DateTime.Now - File.GetCreationTime(f);
            if (age.TotalDays >= recycleDays)
            {
                File.Delete(f);
                count++;
            }
        }
        return Results.Json(new { ok = true, purged = count });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// APIs — Ping / Folders / Jobs (listing)
// ======================================================


    }
}
