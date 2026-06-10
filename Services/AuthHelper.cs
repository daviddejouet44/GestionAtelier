using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace GestionAtelier.Services;

public static class AuthHelper
{
    // ── Token validation ──────────────────────────────────────────────────

    /// <summary>Returns true if the request carries a valid JWT bearer token.</summary>
    public static bool IsAuthenticated(HttpContext ctx)
    {
        return GetPrincipal(ctx) != null;
    }

    /// <summary>Returns true if the request carries a valid JWT with profile == 3 (admin).</summary>
    public static bool IsAdmin(HttpContext ctx)
    {
        var principal = GetPrincipal(ctx);
        if (principal == null) return false;
        var profile = principal.FindFirstValue("profile");
        return profile == "3";
    }

    /// <summary>
    /// Returns the ClaimsPrincipal from a valid JWT bearer token, or null if the token
    /// is missing, malformed, or has an invalid signature.
    /// </summary>
    public static ClaimsPrincipal? GetPrincipal(HttpContext ctx)
    {
        try
        {
            var raw = ctx.Request.Headers["Authorization"].ToString();
            var token = raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? raw.Substring(7).Trim()
                : raw.Trim();

            if (string.IsNullOrWhiteSpace(token)) return null;

            var key = GetSigningKey();
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = false,
                ValidateAudience         = false,
                ClockSkew                = TimeSpan.Zero
            };

            var principal = handler.ValidateToken(token, parameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the value of a named claim from the JWT, or null if the token is invalid.
    /// </summary>
    public static string? GetClaim(HttpContext ctx, string claimType)
        => GetPrincipal(ctx)?.FindFirstValue(claimType);

    // ── Path safety ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="path"/> resolves to a location inside the hotfolders root.
    /// Rejects null / empty paths and any path that escapes the root via ".." sequences.
    /// </summary>
    public static bool IsPathSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = System.IO.Path.GetFullPath(BackendUtils.HotfoldersRoot());
            var full = System.IO.Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ── Signing key ───────────────────────────────────────────────────────

    public static SymmetricSecurityKey GetSigningKey()
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "La variable d'environnement JWT_SECRET est requise. " +
                "Définissez-la avec une clé d'au moins 32 caractères.");
        if (secret.Length < 32)
            throw new InvalidOperationException(
                "JWT_SECRET doit contenir au moins 32 caractères.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
