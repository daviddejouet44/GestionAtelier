using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Services;

public class DailyReportService : BackgroundService
{
    private Timer? _timer;

    private static readonly string[] ProductionStages = new[] { "Impression en cours", "Façonnage" };

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ScheduleNextRun(stoppingToken);
        return Task.CompletedTask;
    }

    private void ScheduleNextRun(CancellationToken ct)
    {
        try
        {
            var config = LoadConfig();
            if (!config.Enabled) return;

            var now = DateTime.Now;
            var todayRun = DateTime.Today.AddHours(config.ReportHour).AddMinutes(config.ReportMinute);
            var nextRun  = todayRun <= now ? todayRun.AddDays(1) : todayRun;
            var delay    = nextRun - now;

            _timer?.Dispose();
            _timer = new Timer(async _ =>
            {
                try
                {
                    await GenerateReportsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] DailyReportService generation error: {ex.Message}");
                }
                if (!ct.IsCancellationRequested)
                    ScheduleNextRun(ct);
            }, null, (long)delay.TotalMilliseconds, Timeout.Infinite);

            Console.WriteLine($"[INFO] DailyReportService scheduled for {nextRun:yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] DailyReportService schedule error: {ex.Message}");
        }
    }

    public static async Task GenerateReportsAsync()
    {
        var config = LoadConfig();
        var outputPath = string.IsNullOrWhiteSpace(config.ReportPath) ? Path.GetTempPath() : config.ReportPath;
        Directory.CreateDirectory(outputPath);

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var reportsMachines  = Path.Combine(outputPath, $"charge_machines_{today}.csv");
        var reportFinitions  = Path.Combine(outputPath, $"charge_finitions_{today}.csv");

        await Task.WhenAll(
            GenerateMachineReportAsync(reportsMachines),
            GenerateFinitionsReportAsync(reportFinitions)
        );

        // Log generated reports
        var logCol = MongoDbHelper.GetCollection<BsonDocument>("dailyReports");
        await logCol.InsertOneAsync(new BsonDocument
        {
            ["generatedAt"]      = DateTime.UtcNow,
            ["machinesReport"]   = reportsMachines,
            ["finitionsReport"]  = reportFinitions
        });

        Console.WriteLine($"[INFO] Daily reports generated: {reportsMachines}, {reportFinitions}");
    }

    private static async Task GenerateMachineReportAsync(string filePath)
    {
        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append("\uFEFF");
        sb.AppendLine("Moteur;Numéro dossier;Nom fichier;Nombre feuilles;Grammage;Format feuille");

        try
        {
            var root = BackendUtils.HotfoldersRoot();

        var stages = ProductionStages;
            var fabCol = MongoDbHelper.GetFabricationsCollection();

            // Get configured print engines for grouping
            var enginesCol = MongoDbHelper.GetCollection<BsonDocument>("printEngines");
            var engines = enginesCol.Find(new BsonDocument()).ToList()
                .Select(e => e.Contains("name") ? e["name"].AsString : "").Where(n => !string.IsNullOrEmpty(n)).ToList();

            foreach (var stage in stages)
            {
                var stageDir = Path.Combine(root, stage);
                if (!Directory.Exists(stageDir)) continue;

                var files = Directory.GetFiles(stageDir, "*.pdf", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(stageDir, "*.PDF", SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var f in files)
                {
                    var fn = Path.GetFileName(f);
                    var doc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fn.ToLowerInvariant()))
                        .SortByDescending(x => x["_id"]).FirstOrDefault();

                    var moteur       = GetString(doc, "moteurImpression") ?? "";
                    var numeroDossier = GetString(doc, "numeroDossier") ?? "";
                    var nombreFeuilles = GetInt(doc, "nombreFeuilles")?.ToString() ?? "";
                    var formatFeuille  = GetString(doc, "formatFeuille") ?? "";
                    var grammage       = ExtractGrammage(doc);

                    sb.AppendLine($"{CsvEscape(moteur)};{CsvEscape(numeroDossier)};{CsvEscape(fn)};{CsvEscape(nombreFeuilles)};{CsvEscape(grammage)};{CsvEscape(formatFeuille)}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Erreur lors de la génération;{ex.Message}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static async Task GenerateFinitionsReportAsync(string filePath)
    {
        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append("\uFEFF");
        sb.AppendLine("Numéro dossier;Nom fichier;Nombre feuilles;Grammage;Format feuille;Options finitions cochées");

        try
        {
            var root = BackendUtils.HotfoldersRoot();
            var stages = ProductionStages;
            var fabCol = MongoDbHelper.GetFabricationsCollection();

            foreach (var stage in stages)
            {
                var stageDir = Path.Combine(root, stage);
                if (!Directory.Exists(stageDir)) continue;

                var files = Directory.GetFiles(stageDir, "*.pdf", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(stageDir, "*.PDF", SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var f in files)
                {
                    var fn = Path.GetFileName(f);
                    var doc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fn.ToLowerInvariant()))
                        .SortByDescending(x => x["_id"]).FirstOrDefault();

                    var numeroDossier  = GetString(doc, "numeroDossier") ?? "";
                    var nombreFeuilles = GetInt(doc, "nombreFeuilles")?.ToString() ?? "";
                    var formatFeuille  = GetString(doc, "formatFeuille") ?? "";
                    var grammage       = ExtractGrammage(doc);

                    // Finition options checked
                    var finitionsDone = new List<string>();
                    if (doc != null && doc.Contains("finitionSteps") && doc["finitionSteps"] != BsonNull.Value
                        && doc["finitionSteps"].IsBsonDocument)
                    {
                        var fs = doc["finitionSteps"].AsBsonDocument;
                        var stepLabels = new Dictionary<string, string>
                        {
                            ["embellissement"] = "Embellissement",
                            ["rainage"]        = "Rainage",
                            ["pliage"]         = "Pliage",
                            ["faconnage"]      = "Façonnage",
                            ["coupe"]          = "Coupe",
                            ["emballage"]      = "Emballage",
                            ["depart"]         = "Départ",
                            ["livraison"]      = "Livraison"
                        };
                        foreach (var kv in stepLabels)
                        {
                            if (fs.Contains(kv.Key) && fs[kv.Key] != BsonNull.Value && fs[kv.Key].IsBsonDocument)
                            {
                                var s = fs[kv.Key].AsBsonDocument;
                                if (s.Contains("done") && s["done"] != BsonNull.Value && s["done"].AsBoolean)
                                    finitionsDone.Add(kv.Value);
                            }
                        }
                    }

                    sb.AppendLine($"{CsvEscape(numeroDossier)};{CsvEscape(fn)};{CsvEscape(nombreFeuilles)};{CsvEscape(grammage)};{CsvEscape(formatFeuille)};{CsvEscape(string.Join(", ", finitionsDone))}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Erreur lors de la génération;{ex.Message}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string CsvEscape(string? v)
    {
        if (v == null) return "";
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n'))
            return $"\"{v.Replace("\"", "\"\"")}\"";
        return v;
    }

    private static string? GetString(BsonDocument? d, string key)
        => d != null && d.Contains(key) && d[key] != BsonNull.Value ? d[key].AsString : null;

    private static int? GetInt(BsonDocument? d, string key)
        => d != null && d.Contains(key) && d[key] != BsonNull.Value ? (int?)d[key].AsInt32 : null;

    private static string ExtractGrammage(BsonDocument? d)
    {
        if (d == null) return "";
        // Try to extract grammage from media names (e.g. "Couché 135g" → "135g")
        foreach (var key in new[] { "media1", "media2", "papier" })
        {
            var v = GetString(d, key);
            if (string.IsNullOrEmpty(v)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(v, @"\d+\s*g");
            if (m.Success) return m.Value.Replace(" ", "");
        }
        return "";
    }

    public static DailyReportConfig LoadConfig()
    {
        try
        {
            return MongoDbHelper.GetSettings<DailyReportConfig>("dailyReportConfig") ?? new DailyReportConfig();
        }
        catch { return new DailyReportConfig(); }
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}
