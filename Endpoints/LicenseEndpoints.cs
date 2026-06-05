using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

public static class LicenseEndpoints
{
    public static void MapLicenseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/license/token", () =>
            Results.Ok(new { token = MachineFingerprint.Generate() }));

        app.MapGet("/api/license/status", () =>
        {
            var info = LicenseService.GetCurrent();
            return Results.Ok(new
            {
                isValid = info.IsValid,
                version = info.Version,
                client = info.Client,
                expireOn = info.IsValid ? info.ExpireOn.ToString("dd/MM/yyyy") : "",
                level = info.Level,
                reason = info.Reason
            });
        });

        app.MapPost("/api/license/activate", async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType)
                return Results.BadRequest(new { error = "Formulaire multipart requis" });

            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.GetFile("licfile");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "Fichier .lic manquant" });

            using var stream = file.OpenReadStream();
            var (ok, error) = LicenseService.SaveLicenseFile(stream);

            if (!ok) return Results.BadRequest(new { error });

            var info = LicenseService.GetCurrent();
            return Results.Ok(new
            {
                message = $"Licence activée — {info.Version} jusqu'au {info.ExpireOn:dd/MM/yyyy}",
                version = info.Version,
                level = info.Level,
                expireOn = info.ExpireOn.ToString("dd/MM/yyyy")
            });
        });
    }
}
