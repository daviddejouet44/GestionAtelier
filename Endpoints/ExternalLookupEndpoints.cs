using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Services;
using GestionAtelier.Endpoints.Settings;

namespace GestionAtelier.Endpoints;

public static class ExternalLookupEndpoints
{
    public static void MapExternalLookupEndpoints(this WebApplication app)
    {
        // ── Auth helpers ──────────────────────────────────────────────────────
        static bool IsAuthenticated(HttpContext ctx)
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                return decoded.Split(':').Length >= 3;
            }
            catch { return false; }
        }

        // ── POST /api/external/{provider}/lookup ─────────────────────────────
        // Body: { ref: "ORDER-12345" }
        // Returns: { ok, fiche: {...prefill fields...}, raw: {...} }
        app.MapPost("/api/external/{provider}/lookup", async (HttpContext ctx, string provider) =>
        {
            if (!IsAuthenticated(ctx))
                return Results.Json(new { ok = false, error = "Authentification requise" });

            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var orderRef = body.TryGetProperty("ref", out var refEl) ? refEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(orderRef))
                    return Results.Json(new { ok = false, error = "Référence requise" });

                var providerLower = provider.ToLowerInvariant();

                // ── Pressero ──────────────────────────────────────────────────
                if (providerLower == "pressero")
                {
                    var intCfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                                 ?? new IntegrationsFullConfig();
                    var pCfg = intCfg.Pressero;
                    if (pCfg == null || !pCfg.Enabled || string.IsNullOrWhiteSpace(pCfg.ApiUrl))
                        return Results.Json(new { ok = false, error = "Pressero non configuré ou désactivé" });

                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    if (!string.IsNullOrEmpty(pCfg.ApiKey))
                        http.DefaultRequestHeaders.Add("X-Api-Key", pCfg.ApiKey);
                    if (!string.IsNullOrEmpty(pCfg.ApiKey) && !string.IsNullOrEmpty(pCfg.ApiSecret))
                    {
                        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{pCfg.ApiKey}:{pCfg.ApiSecret}"));
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                    }

                    var url = $"{pCfg.ApiUrl.TrimEnd('/')}/orders/{Uri.EscapeDataString(orderRef)}";
                    HttpResponseMessage resp;
                    try { resp = await http.GetAsync(url); }
                    catch (Exception ex) { return Results.Json(new { ok = false, error = $"Connexion Pressero échouée: {ex.Message}" }); }

                    if (!resp.IsSuccessStatusCode)
                        return Results.Json(new { ok = false, error = $"Pressero: HTTP {(int)resp.StatusCode}" });

                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var fiche = NormalizePresseroResponse(doc.RootElement);
                    return Results.Json(new { ok = true, fiche, raw = doc.RootElement });
                }

                // ── MDSF ──────────────────────────────────────────────────────
                if (providerLower == "mdsf")
                {
                    var intCfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                                 ?? new IntegrationsFullConfig();
                    var mCfg = intCfg.Mdsf;
                    if (mCfg == null || !mCfg.Enabled || string.IsNullOrWhiteSpace(mCfg.ApiUrl))
                        return Results.Json(new { ok = false, error = "MDSF non configuré ou désactivé" });

                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    if (!string.IsNullOrEmpty(mCfg.ApiKey))
                        http.DefaultRequestHeaders.Add("X-Api-Key", mCfg.ApiKey);

                    var storePrefix = string.IsNullOrEmpty(mCfg.StoreId) ? "" : $"/stores/{mCfg.StoreId}";
                    var url = $"{mCfg.ApiUrl.TrimEnd('/')}{storePrefix}/orders/{Uri.EscapeDataString(orderRef)}";
                    HttpResponseMessage resp;
                    try { resp = await http.GetAsync(url); }
                    catch (Exception ex) { return Results.Json(new { ok = false, error = $"Connexion MDSF échouée: {ex.Message}" }); }

                    if (!resp.IsSuccessStatusCode)
                        return Results.Json(new { ok = false, error = $"MDSF: HTTP {(int)resp.StatusCode}" });

                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var fiche = NormalizeMdsfResponse(doc.RootElement);
                    return Results.Json(new { ok = true, fiche, raw = doc.RootElement });
                }

                // ── Generic ERP ───────────────────────────────────────────────
                {
                    var lookupCfg = MongoDbHelper.GetSettings<SubmissionErpLookupSettings>("submission_erp_lookup")
                                    ?? new SubmissionErpLookupSettings();
                    var src = lookupCfg.ErpSources.FirstOrDefault(s =>
                        s.Id == providerLower || s.Name.ToLowerInvariant() == providerLower);

                    if (src == null)
                        return Results.Json(new { ok = false, error = $"Source ERP inconnue: {provider}" });

                    if (string.IsNullOrWhiteSpace(src.Url))
                        return Results.Json(new { ok = false, error = "URL de la source ERP non configurée" });

                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    ApplyErpAuth(http, src);

                    // Build URL: replace {ref} placeholder or append as query param
                    var url = src.Url.Contains("{ref}")
                        ? src.Url.Replace("{ref}", Uri.EscapeDataString(orderRef))
                        : $"{src.Url.TrimEnd('/')}/{Uri.EscapeDataString(orderRef)}";

                    HttpResponseMessage resp;
                    try { resp = await http.GetAsync(url); }
                    catch (Exception ex) { return Results.Json(new { ok = false, error = $"Connexion ERP échouée: {ex.Message}" }); }

                    if (!resp.IsSuccessStatusCode)
                        return Results.Json(new { ok = false, error = $"ERP: HTTP {(int)resp.StatusCode}" });

                    var content = await resp.Content.ReadAsStringAsync();
                    var fiche   = new Dictionary<string, string>();

                    if (src.ResponseFormat == "xml")
                    {
                        try
                        {
                            var xDoc = XDocument.Parse(content);
                            var orderEl = xDoc.Descendants("Order")
                                              .Concat(xDoc.Descendants("Commande"))
                                              .Concat(xDoc.Descendants("Job"))
                                              .FirstOrDefault()
                                          ?? xDoc.Root;
                            if (orderEl != null)
                            {
                                foreach (var kv in src.Mapping)
                                {
                                    var el = orderEl.Element(kv.Value) ?? orderEl.Descendants(kv.Value).FirstOrDefault();
                                    if (el != null) fiche[kv.Key] = el.Value;
                                }
                                foreach (var el in orderEl.Elements())
                                    if (!fiche.ContainsKey(el.Name.LocalName))
                                        fiche[el.Name.LocalName] = el.Value;
                            }
                        }
                        catch (Exception ex) { return Results.Json(new { ok = false, error = $"Erreur parsing XML ERP: {ex.Message}" }); }
                        return Results.Json(new { ok = true, fiche, raw = (object?)null });
                    }
                    else
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(content);
                            fiche = ApplyJsonMapping(doc.RootElement, src.Mapping);
                            return Results.Json(new { ok = true, fiche, raw = doc.RootElement });
                        }
                        catch (Exception ex) { return Results.Json(new { ok = false, error = $"Erreur parsing JSON ERP: {ex.Message}" }); }
                    }
                }
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── GET /api/external/detect-ref ─────────────────────────────────────
        // ?filename=ORDER-12345_myfile.pdf  →  { ok, ref }
        app.MapGet("/api/external/detect-ref", (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx)) return Results.Json(new { ok = false, error = "Authentification requise" });
            try
            {
                var filename  = ctx.Request.Query["filename"].ToString();
                var lookupCfg = MongoDbHelper.GetSettings<SubmissionErpLookupSettings>("submission_erp_lookup")
                                ?? new SubmissionErpLookupSettings();
                var regex = lookupCfg.RefDetectionRegex;
                if (string.IsNullOrWhiteSpace(regex) || string.IsNullOrWhiteSpace(filename))
                    return Results.Json(new { ok = false, detected = (string?)null });

                var m = Regex.Match(filename, regex, RegexOptions.IgnoreCase);
                if (!m.Success)
                    return Results.Json(new { ok = false, detected = (string?)null });

                // Return first capture group if present, else full match
                var detected = m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
                return Results.Json(new { ok = true, detected });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }

    // ── Normalization helpers ─────────────────────────────────────────────────

    private static Dictionary<string, string> NormalizePresseroResponse(JsonElement root)
    {
        var fiche = new Dictionary<string, string>();
        // Pressero common field names → fiche field names
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"]             = "referenceCommande",
            ["orderNumber"]         = "referenceCommande",
            ["id"]                  = "referenceCommande",
            ["customerName"]        = "nomClient",
            ["customer"]            = "nomClient",
            ["companyName"]         = "client",
            ["productName"]         = "typeTravail",
            ["name"]                = "typeTravail",
            ["quantity"]            = "quantite",
            ["qty"]                 = "quantite",
            ["format"]              = "formatFini",
            ["size"]                = "formatFini",
            ["comments"]            = "commentaire",
            ["notes"]               = "commentaire",
            ["requiredDate"]        = "dateLivraisonSouhaitee",
            ["dueDate"]             = "dateLivraisonSouhaitee",
            ["orderDate"]           = "dateReceptionSouhaitee",
        };
        FlattenJson(root, "", (path, value) =>
        {
            var key = path.Split('.').Last();
            if (map.TryGetValue(key, out var ficheField))
                fiche.TryAdd(ficheField, value);
        });
        return fiche;
    }

    private static Dictionary<string, string> NormalizeMdsfResponse(JsonElement root)
    {
        var fiche = new Dictionary<string, string>();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"]             = "referenceCommande",
            ["orderNumber"]         = "referenceCommande",
            ["customerName"]        = "nomClient",
            ["companyName"]         = "client",
            ["productDescription"]  = "typeTravail",
            ["quantity"]            = "quantite",
            ["trimSize"]            = "formatFini",
            ["deliveryDate"]        = "dateLivraisonSouhaitee",
            ["orderDate"]           = "dateReceptionSouhaitee",
            ["specialInstructions"] = "commentaire",
        };
        FlattenJson(root, "", (path, value) =>
        {
            var key = path.Split('.').Last();
            if (map.TryGetValue(key, out var ficheField))
                fiche.TryAdd(ficheField, value);
        });
        return fiche;
    }

    private static Dictionary<string, string> ApplyJsonMapping(JsonElement root, Dictionary<string, string> mapping)
    {
        var fiche = new Dictionary<string, string>();
        // mapping: ficheField → jsonPath (dot notation e.g. "order.customerName")
        var flat  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(root, "", (path, value) => flat[path] = value);

        foreach (var kv in mapping)
        {
            var jsonPath = kv.Value;
            if (flat.TryGetValue(jsonPath, out var v))
                fiche[kv.Key] = v;
            else
            {
                // Try last segment match
                var key = jsonPath.Split('.').Last();
                var found = flat.FirstOrDefault(x => x.Key.EndsWith("." + key, StringComparison.OrdinalIgnoreCase)
                                                   || x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (found.Value != null) fiche[kv.Key] = found.Value;
            }
        }
        return fiche;
    }

    private static void FlattenJson(JsonElement el, string prefix, Action<string, string> visitor)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    FlattenJson(prop.Value, string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}", visitor);
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in el.EnumerateArray())
                    FlattenJson(item, $"{prefix}[{i++}]", visitor);
                break;
            default:
                visitor(prefix, el.ValueKind == JsonValueKind.Null ? "" : el.ToString());
                break;
        }
    }

    private static void ApplyErpAuth(HttpClient http, ErpSourceConfig src)
    {
        switch (src.AuthType.ToLowerInvariant())
        {
            case "basic":
                if (!string.IsNullOrEmpty(src.AuthUser))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{src.AuthUser}:{src.AuthPassword}"));
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }
                break;
            case "bearer":
                // Use AuthToken if set, otherwise fall back to AuthPassword (UI stores it in authPassword field)
                var bearerToken = !string.IsNullOrEmpty(src.AuthToken) ? src.AuthToken : src.AuthPassword;
                if (!string.IsNullOrEmpty(bearerToken))
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                break;
            case "apikey":
                var headerName = string.IsNullOrEmpty(src.AuthHeader) ? "X-Api-Key" : src.AuthHeader;
                // Use AuthToken if set, otherwise fall back to AuthPassword (UI stores it in authPassword field)
                var apiKeyValue = !string.IsNullOrEmpty(src.AuthToken) ? src.AuthToken : src.AuthPassword;
                if (!string.IsNullOrEmpty(apiKeyValue))
                    http.DefaultRequestHeaders.TryAddWithoutValidation(headerName, apiKeyValue);
                break;
        }
    }
}
