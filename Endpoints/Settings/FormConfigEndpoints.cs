using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

public static class FormConfigEndpoints
{
    private const string SettingsKey = "formConfig";

    // Cache the default config so it is only built once per process lifetime.
    public static readonly FabricationFormConfig DefaultConfig = BuildDefaultConfig();

    public static void MapFormConfigEndpoints(this WebApplication app)
    {
        // GET /api/settings/form-config
        // Returns the saved config or the built-in default if none exists
        app.MapGet("/api/settings/form-config", () =>
        {
            try
            {
                var saved = MongoDbHelper.GetSettings<FabricationFormConfig>(SettingsKey);
                if (saved == null) return Results.Json(DefaultConfig);

                // Merge: add any default fields that are missing from the saved config
                var savedIds = new HashSet<string>(saved.Fields.Select(f => f.Id));
                var missing = DefaultConfig.Fields.Where(f => !savedIds.Contains(f.Id)).ToList();
                if (missing.Count > 0)
                {
                    saved.Fields.AddRange(missing);
                    // Also add missing sections to the saved config
                    var savedSections = new HashSet<string>(saved.Sections);
                    foreach (var s in DefaultConfig.Sections.Where(s => !savedSections.Contains(s)))
                        saved.Sections.Add(s);
                }

                // Migration: move mailValidationDevis to "Informations générales" if still in "BAT"
                var devisField = saved.Fields.FirstOrDefault(f => f.Id == "mailValidationDevis");
                if (devisField != null && devisField.Section == "BAT")
                    devisField.Section = "Informations générales";

                // Key planning dates must remain editable from the production sheet.
                foreach (var field in saved.Fields.Where(f =>
                    f.Id == "dateEnvoi" ||
                    f.Id == "dateProductionFinitions" ||
                    f.Id == "dateImpression"))
                {
                    field.ReadOnly = false;
                }

                // Ensure dateReception is always visible and in the correct section.
                var recField = saved.Fields.FirstOrDefault(f => f.Id == "dateReception");
                if (recField != null)
                {
                    recField.Visible = true;
                    recField.Section = "Dates clés";
                    recField.ReadOnly = false;
                }

                return Results.Json(saved);
            }
            catch (Exception ex)
            {
                return ErrorHelper.HandleException(ex);
            }
        });

        // PUT /api/settings/form-config  (admin only)
        app.MapPut("/api/settings/form-config", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var config = await ctx.Request.ReadFromJsonAsync<FabricationFormConfig>();
                if (config == null)
                    return Results.Json(new { ok = false, error = "Payload invalide" });

                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ErrorHelper.HandleException(ex);
            }
        });

        // DELETE /api/settings/form-config  — resets to default (admin only)
        app.MapDelete("/api/settings/form-config", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                MongoDbHelper.DeleteSettings(SettingsKey);
                return Results.Json(new { ok = true, config = DefaultConfig });
            }
            catch (Exception ex)
            {
                return ErrorHelper.HandleException(ex);
            }
        });

        // POST /api/settings/form-config/section  — add a new custom section
        app.MapPost("/api/settings/form-config/section", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var name = body.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(name))
                    return Results.Json(new { ok = false, error = "Nom de section requis" });

                var config = MongoDbHelper.GetSettings<FabricationFormConfig>(SettingsKey) ?? DefaultConfig;
                if (config.Sections.Contains(name))
                    return Results.Json(new { ok = false, error = "Section déjà existante" });

                config.Sections.Add(name);
                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // DELETE /api/settings/form-config/section/{name}  — remove a section (fields moved to first section)
        app.MapDelete("/api/settings/form-config/section/{name}", (HttpContext ctx, string name) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var decodedName = Uri.UnescapeDataString(name);
                var config = MongoDbHelper.GetSettings<FabricationFormConfig>(SettingsKey) ?? DefaultConfig;
                config.Sections.Remove(decodedName);
                // Move orphaned fields to first available section
                var fallback = config.Sections.FirstOrDefault() ?? "Informations générales";
                foreach (var f in config.Fields.Where(f => f.Section == decodedName))
                    f.Section = fallback;
                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // POST /api/settings/form-config/field  — add a custom field
        app.MapPost("/api/settings/form-config/field", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var field = await ctx.Request.ReadFromJsonAsync<FormFieldConfig>();
                if (field == null || string.IsNullOrWhiteSpace(field.Id))
                    return Results.Json(new { ok = false, error = "Champ invalide" });

                var config = MongoDbHelper.GetSettings<FabricationFormConfig>(SettingsKey) ?? DefaultConfig;
                if (config.Fields.Any(f => f.Id == field.Id))
                    return Results.Json(new { ok = false, error = "Un champ avec cet ID existe déjà" });

                field.IsCustom = true;
                field.Order = config.Fields.Count > 0 ? config.Fields.Max(f => f.Order) + 1 : 0;
                config.Fields.Add(field);

                // Add section if it doesn't exist
                if (!string.IsNullOrWhiteSpace(field.Section) && !config.Sections.Contains(field.Section))
                    config.Sections.Add(field.Section);

                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // DELETE /api/settings/form-config/field/{id}  — delete a custom field
        app.MapDelete("/api/settings/form-config/field/{id}", (HttpContext ctx, string id) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var config = MongoDbHelper.GetSettings<FabricationFormConfig>(SettingsKey) ?? DefaultConfig;
                var field = config.Fields.FirstOrDefault(f => f.Id == id);
                if (field == null)
                    return Results.Json(new { ok = false, error = "Champ introuvable" });
                if (!field.IsCustom)
                    return Results.Json(new { ok = false, error = "Seuls les champs personnalisés peuvent être supprimés" });

                config.Fields.Remove(field);
                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }

    /// <summary>
    /// Returns the built-in default configuration that mirrors the hard-coded form layout
    /// from before the dynamic-form feature was introduced.
    /// </summary>
    public static FabricationFormConfig BuildDefaultConfig()
    {
        int order = 0;

        var fields = new List<FormFieldConfig>
        {
            // ── Section : Informations générales ──────────────────────────
            new() { Id = "numeroDossier",   Label = "Numéro de dossier",       Type = "text",   Section = "Informations générales", Order = order++, Visible = true,  Required = true,  Width = "half" },
            new() { Id = "client",          Label = "Client",                  Type = "text",   Section = "Informations générales", Order = order++, Visible = true,  Width = "half" },
            new() { Id = "operateur",       Label = "Opérateur",               Type = "text",   Section = "Informations générales", Order = order++, Visible = true,  ReadOnly = true, Width = "half" },
            new() { Id = "typeTravail",     Label = "Type de travail",         Type = "select", Section = "Informations générales", Order = order++, Visible = true,  Required = true,  Width = "half" },
            new() { Id = "formatFini",      Label = "Format fini",             Type = "text",   Section = "Informations générales", Order = order++, Visible = true,  Width = "half" },
            new() { Id = "quantite",        Label = "Quantité",                Type = "number", Section = "Informations générales", Order = order++, Visible = true,  Width = "half" },
            new() { Id = "moteurImpression",Label = "Moteur d'impression",     Type = "select", Section = "Informations générales", Order = order++, Visible = true,  Width = "half" },
            new() { Id = "certification",   Label = "Certification",           Type = "select", Section = "Informations générales", Order = order++, Visible = true,  Width = "half",
                    Options = new List<string> { "Aucun", "FSC", "PEFC" } },

            // ── Section : Donneur d'ordre ────────────────────────────────
            new() { Id = "donneurOrdreNom",       Label = "Nom",       Type = "text", Section = "Donneur d'ordre", Order = order++, Visible = true, Width = "half" },
            new() { Id = "donneurOrdrePrenom",    Label = "Prénom",    Type = "text", Section = "Donneur d'ordre", Order = order++, Visible = true, Width = "half" },
            new() { Id = "donneurOrdreTelephone", Label = "Téléphone", Type = "text", Section = "Donneur d'ordre", Order = order++, Visible = true, Width = "half" },
            new() { Id = "donneurOrdreEmail",     Label = "Email",     Type = "text", Section = "Donneur d'ordre", Order = order++, Visible = true, Width = "half" },

            // ── Section : Impression ─────────────────────────────────────
            new() { Id = "rectoVerso",          Label = "Recto/Verso",              Type = "select", Section = "Impression", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Recto", "Recto/Verso" } },
            new() { Id = "formeDecoupe",        Label = "Forme de découpe",         Type = "text",   Section = "Impression", Order = order++, Visible = true, Width = "half" },
            new() { Id = "pagination",          Label = "Pagination",               Type = "text",   Section = "Impression", Order = order++, Visible = true, Width = "half" },
            new() { Id = "formatFeuilleMachine",Label = "Format feuille en machine",Type = "select", Section = "Impression", Order = order++, Visible = true, Width = "half" },
            new() { Id = "preflightProfil",     Label = "Preflight utilisé",        Type = "text",   Section = "Impression", Order = order++, Visible = true, ReadOnly = true, Width = "half" },

            // ── Section : Media ──────────────────────────────────────────
            new() { Id = "media1",          Label = "Média 1",           Type = "select", Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media1Fabricant", Label = "Fabricant média 1", Type = "text",   Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media2",          Label = "Média 2",           Type = "select", Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media2Fabricant", Label = "Fabricant média 2", Type = "text",   Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media3",          Label = "Média 3",           Type = "select", Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media3Fabricant", Label = "Fabricant média 3", Type = "text",   Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media4",          Label = "Média 4",           Type = "select", Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "media4Fabricant", Label = "Fabricant média 4", Type = "text",   Section = "Media", Order = order++, Visible = true, Width = "half" },
            new() { Id = "couvertureMedia",     Label = "Média couverture",    Type = "select", Section = "Media", Order = order++, Visible = true, Width = "half",
                    DependsOn = "typeTravail" },
            new() { Id = "couvertureFabricant", Label = "Fabricant couverture",Type = "text",   Section = "Media", Order = order++, Visible = true, Width = "half",
                    DependsOn = "typeTravail" },

            // ── Section : BAT ─────────────────────────────────────────────
            new() { Id = "bat",               Label = "BAT",                    Type = "select",      Section = "BAT", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Non", "Numérique", "Papier" } },
            new() { Id = "dateEnvoiBat",      Label = "Date d'envoi du BAT au client", Type = "date", Section = "BAT", Order = order++, Visible = true, Width = "half" },
            new() { Id = "mailValidationBat", Label = "Mail validation BAT",   Type = "file-import", Section = "BAT", Order = order++, Visible = true, Width = "half" },
            new() { Id = "mailValidationDevis",Label = "Mail validation devis", Type = "file-import", Section = "Informations générales", Order = order++, Visible = true, Width = "half" },

            // ── Section : Finitions ───────────────────────────────────────
            new() { Id = "rainage",         Label = "Rainage",          Type = "checkbox",    Section = "Finitions", Order = order++, Visible = true, Width = "half" },
            new() { Id = "ennoblissement",  Label = "Ennoblissement",   Type = "multiselect", Section = "Finitions", Order = order++, Visible = true, Width = "full" },
            new() { Id = "faconnageBinding",Label = "Type de reliure",  Type = "select",      Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Aucune", "Piqûre 2 points", "Dos carré collé", "Spirale plastique", "Wire-O", "Reliure suisse", "Reliure cousue" } },
            new() { Id = "plis",    Label = "Plis",   Type = "select", Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Pli accordéon","Pli roulé","Pli fenêtre" } },
            new() { Id = "sortie",  Label = "Sortie", Type = "select", Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "À plat","Assemblée" } },

            // ── Section : Production ──────────────────────────────────────
            new() { Id = "nombreFeuilles",  Label = "Nombre de feuilles", Type = "calculated", Section = "Production", Order = order++, Visible = true, ReadOnly = true, Width = "half",
                    CalculationRule = "quantite/typeTravail" },

            // ── Section : Passes (regroupé dans Production) ───────────────
            new() { Id = "passes", Label = "Passes (feuilles supplémentaires)", Type = "calculated", Section = "Production", Order = order++, Visible = true, ReadOnly = true, Width = "full" },

            // ── Process d'impression ──────────────────────────────────────
            new() { Id = "process",  Label = "Process",  Type = "select", Section = "Production", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Numérique", "Offset" } },
            new() { Id = "bascule",  Label = "Bascule",  Type = "select", Section = "Production", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Non", "In 8", "In 12" }, DependsOn = "process", DependsOnValue = "Offset" },
            new() { Id = "couleurs", Label = "Couleurs", Type = "select", Section = "Production", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "1 couleur", "Bichromie", "Trichromie", "Quadri" } },
            new() { Id = "couleursAccompagnement", Label = "Couleurs d'accompagnement", Type = "text", Section = "Production", Order = order++, Visible = true, Width = "half" },

            // ── Section : Livraison ───────────────────────────────────────
            new() { Id = "retraitLivraison", Label = "Retrait / livraison", Type = "select", Section = "Livraison", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Retrait imprimerie","Livraison" } },
            new() { Id = "adresseLivraison", Label = "Adresse de livraison", Type = "text",   Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "justifsQuantite",  Label = "Quantité justifs",     Type = "number", Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "justifsAdresse",   Label = "Adresse justifs",      Type = "text",   Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "repartitions",     Label = "Répartitions et quantités", Type = "group", Section = "Livraison", Order = order++, Visible = true, Width = "full" },

            // ── Section : Notes ───────────────────────────────────────────
            new() { Id = "notes", Label = "Notes / Observations", Type = "textarea", Section = "Notes", Order = order++, Visible = true, Width = "full" },

            // ── Section : Dates clés ──────────────────────────────────────
            new() { Id = "dateReception",           Label = "Date de réception souhaitée",  Type = "date",     Section = "Dates clés", Order = order++, Visible = true, Width = "half" },
            new() { Id = "dateEnvoi",               Label = "Date d'envoi",                 Type = "date",     Section = "Dates clés", Order = order++, Visible = true, Width = "half" },
            new() { Id = "dateProductionFinitions", Label = "Date production Finitions",    Type = "date",     Section = "Dates clés", Order = order++, Visible = true, Width = "half" },
            new() { Id = "dateImpression",          Label = "Date d'impression",            Type = "date",     Section = "Dates clés", Order = order++, Visible = true, Width = "half" },

            // ── Section : Temps de production ────────────────────────────
            new() { Id = "tempsProduitMinutes", Label = "Temps théorique de production (minutes)", Type = "number", Section = "Temps de production", Order = order++, Visible = true, Width = "half" },
        };

        var sections = new List<string>
        {
            "Informations générales",
            "Donneur d'ordre",
            "Impression",
            "Media",
            "BAT",
            "Finitions",
            "Production",
            "Livraison",
            "Notes",
            "Dates clés",
            "Temps de production"
        };

        return new FabricationFormConfig { Fields = fields, Sections = sections };
    }
}
