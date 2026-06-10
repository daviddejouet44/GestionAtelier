using System;
using Microsoft.AspNetCore.Http;

namespace GestionAtelier.Services;

public static class ErrorHelper
{
    public static IResult HandleException(Exception ex, string context = "")
    {
        Console.WriteLine($"[ERROR] {context}: {ex}");
        return Results.Json(new { ok = false, error = "Une erreur interne est survenue. Veuillez réessayer." }, statusCode: 500);
    }
}
