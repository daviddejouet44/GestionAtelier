using System;
using System.Security.Cryptography;
using System.Text;

namespace GestionAtelier.Services;

public static class MachineFingerprint
{
    public static string Generate()
    {
        string raw = $"{Environment.MachineName}-{Environment.UserName}-GestionAtelier";
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(hash)[..24];
    }
}
