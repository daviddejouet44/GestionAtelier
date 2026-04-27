namespace GestionAtelier.Constants;

public static class StageConstants
{
    public static readonly Dictionary<string, int> StageProgress = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Début de production", 0 },
        { "1.Reception", 0 },
        { "Corrections", 15 },
        { "Corrections et fond perdu", 15 },
        { "Prêt pour impression", 25 },
        { "6.Archivage", 25 },
        { "BAT", 35 },
        { "4.BAT", 35 },
        { "PrismaPrepare", 50 },
        { "Fiery", 50 },
        { "Impression en cours", 65 },
        { "Façonnage", 80 },
        { "Fin de production", 100 }
    };

    public static int GetProgress(string? stage)
    {
        if (string.IsNullOrEmpty(stage)) return 0;
        foreach (var kv in StageProgress)
            if (stage.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                return kv.Value;
        return 0;
    }
}
