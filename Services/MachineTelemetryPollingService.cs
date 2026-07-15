using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GestionAtelier.Services;

// ======================================================
// Pilotage machines (point 8) — polling HTTP (pull)
// Interroge périodiquement l'URL de statut des machines configurées en
// protocol "http" et ingère la télémétrie (statut, compteur, OF, etc.).
// ======================================================
public class MachineTelemetryPollingService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly Dictionary<string, DateTime> _lastPoll = new(StringComparer.OrdinalIgnoreCase);

    public MachineTelemetryPollingService(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse l'app démarrer avant le premier cycle.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnce(stoppingToken); }
            catch (Exception ex) { Console.WriteLine($"[WARN] MachinePolling: {ex.Message}"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { }
        }
    }

    private async Task PollOnce(CancellationToken ct)
    {
        List<BsonDocument> conns;
        try
        {
            conns = MongoDbHelper.GetCollection<BsonDocument>("machineConnections")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("protocol", "http"),
                    Builders<BsonDocument>.Filter.Eq("enabled", true)))
                .ToList();
        }
        catch { return; }

        var now = DateTime.UtcNow;
        foreach (var c in conns)
        {
            if (ct.IsCancellationRequested) return;
            var moteur = c.Contains("moteur") && c["moteur"].IsString ? c["moteur"].AsString : "";
            var address = c.Contains("address") && c["address"].IsString ? c["address"].AsString : "";
            if (string.IsNullOrWhiteSpace(moteur) || string.IsNullOrWhiteSpace(address)) continue;
            int interval = 30;
            try { if (c.Contains("pollIntervalSec") && c["pollIntervalSec"] != BsonNull.Value) interval = c["pollIntervalSec"].ToInt32(); } catch { }
            interval = Math.Clamp(interval, 5, 3600);

            if (_lastPoll.TryGetValue(moteur, out var last) && (now - last).TotalSeconds < interval) continue;
            _lastPoll[moteur] = now;

            try
            {
                var client = _httpFactory.CreateClient("external");
                client.Timeout = TimeSpan.FromSeconds(8);
                var body = await client.GetStringAsync(address, ct);
                var json = JsonDocument.Parse(body).RootElement;
                if (json.ValueKind != JsonValueKind.Object) continue;

                string? statut = json.TryGetProperty("statut", out var s) ? s.GetString() : null;
                long? compteur = json.TryGetProperty("compteurFeuilles", out var cc) && cc.TryGetInt64(out var cv) ? cv : (long?)null;
                string? ofEnCours = json.TryGetProperty("ofEnCours", out var o) ? o.GetString() : null;
                int? temps = json.TryGetProperty("tempsRestantMinutes", out var t) && t.TryGetInt32(out var tv) ? tv : (int?)null;
                string? note = json.TryGetProperty("note", out var n) ? n.GetString() : null;

                MachineTelemetryService.Ingest(moteur, statut, compteur, ofEnCours, temps, note, source: "http", by: "poller");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Poll {moteur} ({address}): {ex.Message}");
            }
        }
    }
}
