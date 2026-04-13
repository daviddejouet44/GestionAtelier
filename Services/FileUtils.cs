using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class FileUtils
{
    public static readonly Regex SafeNameRegex =
        new(@"[^\w\-]", RegexOptions.Compiled);

    public static string HotfoldersRoot()
    {
        var env = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        return @"C:\Flux";
    }

    public static string[] Hotfolders()
    {
        try
        {
            var root = HotfoldersRoot();
            if (!Directory.Exists(root)) return Array.Empty<string>();

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
}
