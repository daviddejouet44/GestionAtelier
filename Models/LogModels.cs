using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
}

public class ActivityLogEntry
{
    public DateTime Timestamp { get; set; }
    public string UserLogin { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

