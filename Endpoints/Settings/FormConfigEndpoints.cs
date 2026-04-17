using System;
using System.Collections.Generic;
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
                var config = saved ?? DefaultConfig;
                return Results.Json(config);
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // PUT /api/settings/form-config  (admin only)
        app.MapPut("/api/settings/form-config", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || parts[2] != "3")
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var config = await ctx.Request.ReadFromJsonAsync<FabricationFormConfig>();
                if (config == null)
                    return Results.Json(new { ok = false, error = "Payload invalide" });

                MongoDbHelper.UpsertSettings(SettingsKey, config);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // DELETE /api/settings/form-config  — resets to default (admin only)
        app.MapDelete("/api/settings/form-config", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || parts[2] != "3")
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                MongoDbHelper.DeleteSettings(SettingsKey);
                return Results.Json(new { ok = true, config = DefaultConfig });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
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
            new() { Id = "mailValidationBat", Label = "Mail validation BAT",   Type = "file-import", Section = "BAT", Order = order++, Visible = true, Width = "half" },
            new() { Id = "mailValidationDevis",Label = "Mail validation devis", Type = "file-import", Section = "BAT", Order = order++, Visible = true, Width = "half" },

            // ── Section : Finitions ───────────────────────────────────────
            new() { Id = "rainage",         Label = "Rainage",          Type = "checkbox",    Section = "Finitions", Order = order++, Visible = true, Width = "half" },
            new() { Id = "ennoblissement",  Label = "Ennoblissement",   Type = "multiselect", Section = "Finitions", Order = order++, Visible = true, Width = "full" },
            new() { Id = "faconnageBinding",Label = "Type de reliure",  Type = "select",      Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "2 piques métal","2 piques à plat","2 piques booklet","Dos carré collé","Dos carré piqué","2 piques calendrier (à l'italienne)","Wire'O" } },
            new() { Id = "plis",    Label = "Plis",   Type = "select", Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Pli accordéon","Pli roulé","Pli fenêtre" } },
            new() { Id = "sortie",  Label = "Sortie", Type = "select", Section = "Finitions", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "À plat","Assemblée" } },

            // ── Section : Production ──────────────────────────────────────
            new() { Id = "nombreFeuilles",  Label = "Nombre de feuilles", Type = "calculated", Section = "Production", Order = order++, Visible = true, ReadOnly = true, Width = "half",
                    CalculationRule = "quantite/typeTravail" },

            // ── Section : Passes (regroupé dans Production) ───────────────
            new() { Id = "passes", Label = "Passes (feuilles supplémentaires)", Type = "calculated", Section = "Production", Order = order++, Visible = true, ReadOnly = true, Width = "full" },

            // ── Section : Livraison ───────────────────────────────────────
            new() { Id = "retraitLivraison", Label = "Retrait / livraison", Type = "select", Section = "Livraison", Order = order++, Visible = true, Width = "half",
                    Options = new List<string> { "Retrait imprimerie","Livraison" } },
            new() { Id = "adresseLivraison", Label = "Adresse de livraison", Type = "text",   Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "justifsQuantite",  Label = "Quantité justifs",     Type = "number", Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "justifsAdresse",   Label = "Adresse justifs",      Type = "text",   Section = "Livraison", Order = order++, Visible = true, Width = "half" },
            new() { Id = "repartitions",     Label = "Répartitions et quantités", Type = "group", Section = "Livraison", Order = order++, Visible = true, Width = "full" },

            // ── Section : Notes ───────────────────────────────────────────
            new() { Id = "notes", Label = "Notes / Observations", Type = "textarea", Section = "Notes", Order = order++, Visible = true, Width = "full" },
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
            "Notes"
        };

        return new FabricationFormConfig { Fields = fields, Sections = sections };
    }
}
