using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GestionAtelier.Services;

/// <summary>
/// Chiffrement / déchiffrement AES-256-CBC pour stocker les credentials en base.
/// La clé est dérivée d'une variable d'environnement GA_ENCRYPTION_KEY
/// (ou d'une valeur par défaut si non configurée — à remplacer en production).
/// </summary>
public static class CredentialCrypto
{
    // 32-byte key (256 bits). Override via env var GA_ENCRYPTION_KEY (hex 64 chars).
    private static byte[] DeriveKey()
    {
        var envKey = Environment.GetEnvironmentVariable("GA_ENCRYPTION_KEY");
        if (!string.IsNullOrWhiteSpace(envKey) && envKey.Length >= 32)
        {
            // Use first 32 chars as UTF-8 key bytes (padded/trimmed to 32)
            var raw = Encoding.UTF8.GetBytes(envKey);
            var key = new byte[32];
            Buffer.BlockCopy(raw, 0, key, 0, Math.Min(raw.Length, 32));
            return key;
        }
        // Fallback: warn administrators that they should set GA_ENCRYPTION_KEY in production
        Console.WriteLine("[WARN] CredentialCrypto: GA_ENCRYPTION_KEY not set. Using default fallback key. " +
                          "Set GA_ENCRYPTION_KEY (>=32 chars) in production to strengthen credential encryption.");
        using var deriv = new Rfc2898DeriveBytes(
            "GestionAtelierOrderSourceKey!2024",
            Encoding.UTF8.GetBytes("GA_OS_SALT_v1"),
            100_000,
            HashAlgorithmName.SHA256);
        return deriv.GetBytes(32);
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);           // prepend IV (16 bytes)
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
            sw.Write(plainText);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static string Decrypt(string cipherBase64)
    {
        if (string.IsNullOrEmpty(cipherBase64)) return "";
        try
        {
            var cipherBytes = Convert.FromBase64String(cipherBase64);
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            var iv = new byte[16];
            Buffer.BlockCopy(cipherBytes, 0, iv, 0, 16);
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipherBytes, 16, cipherBytes.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }
}
