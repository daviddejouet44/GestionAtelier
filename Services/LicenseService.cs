using System;
using System.IO;
using System.Linq;
using Standard.Licensing;
using Standard.Licensing.Validation;

namespace GestionAtelier.Services;

public class LicenseInfo
{
    public bool IsValid { get; set; }
    public string Version { get; set; } = "";
    public string Client { get; set; } = "";
    public DateTime ExpireOn { get; set; }
    public string Reason { get; set; } = "";
    public int Level { get; set; }
}

public static class LicenseService
{
    private static readonly string PublicKey =
        Environment.GetEnvironmentVariable("LICENSE_PUBLIC_KEY")
        ?? ReadPublicKeyFile()
        ?? throw new InvalidOperationException(
            "La clé publique de licence est manquante. " +
            "Définissez la variable d'environnement LICENSE_PUBLIC_KEY " +
            "ou placez le fichier data/public.key dans le répertoire de l'application.");

    private static string? ReadPublicKeyFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "public.key");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static readonly string LicensePath =
        Path.Combine(AppContext.BaseDirectory, "data", "license.lic");

    private static volatile LicenseInfo? _cached;
    private static readonly object _lock = new object();

    public static LicenseInfo GetCurrent()
    {
        if (_cached != null) return _cached;
        lock (_lock)
        {
            if (_cached != null) return _cached;
            _cached = Load();
            return _cached;
        }
    }

    public static void Invalidate()
    {
        lock (_lock) { _cached = null; }
    }

    public static LicenseInfo Load()
    {
        if (!File.Exists(LicensePath))
            return Invalid("Aucune licence installée");

        try
        {
            using var stream = File.OpenRead(LicensePath);
            var license = License.Load(stream);

            var failures = license.Validate()
                .Signature(PublicKey)
                .AssertValidLicense()
                .ToList();

            if (failures.Any())
                return Invalid("Licence invalide ou falsifiée");

            string expectedToken = MachineFingerprint.Generate();
            string licenseToken = license.ProductFeatures.Get("MachineToken") ?? "";

            if (licenseToken != expectedToken)
                return Invalid("Licence non valide pour cette machine");

            if (license.Expiration < DateTime.Now)
                return Invalid($"Licence expirée depuis le {license.Expiration:dd/MM/yyyy}");

            string version = license.ProductFeatures.Get("Version") ?? "";
            return new LicenseInfo
            {
                IsValid = true,
                Version = version,
                Client = license.ProductFeatures.Get("Client") ?? "",
                ExpireOn = license.Expiration,
                Level = VersionToLevel(version),
                Reason = ""
            };
        }
        catch (Exception ex)
        {
            return Invalid($"Erreur lecture licence : {ex.Message}");
        }
    }

    public static (bool ok, string error) SaveLicenseFile(Stream stream)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
        var tmp = LicensePath + ".tmp";
        try
        {
            using (var fs = File.Create(tmp))
                stream.CopyTo(fs);

            using (var fs = File.OpenRead(tmp))
            {
                var lic = License.Load(fs);
                var failures = lic.Validate().Signature(PublicKey).AssertValidLicense().ToList();
                if (failures.Any()) { File.Delete(tmp); return (false, "Signature invalide"); }
                if (lic.Expiration < DateTime.Now) { File.Delete(tmp); return (false, "Licence expirée"); }
                string tok = lic.ProductFeatures.Get("MachineToken") ?? "";
                if (tok != MachineFingerprint.Generate()) { File.Delete(tmp); return (false, "Licence non valide pour cette machine"); }
            }

            File.Move(tmp, LicensePath, overwrite: true);
            Invalidate();
            return (true, "");
        }
        catch (Exception ex)
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            return (false, ex.Message);
        }
    }

    private static LicenseInfo Invalid(string reason) =>
        new() { IsValid = false, Reason = reason, Level = 0 };

    private static int VersionToLevel(string v) => v switch
    {
        "GestionAtelier Starter" => 1,
        "GestionAtelier Pro" => 2,
        "GestionAtelier Enterprise" => 3,
        _ => 0
    };
}
