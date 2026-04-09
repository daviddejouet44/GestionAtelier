namespace GestionAtelier.Models;

public record AssignmentItem
{
    public string FullPath     { get; init; } = default!;
    public string FileName     { get; init; } = "";
    public string OperatorId   { get; init; } = default!;
    public string OperatorName { get; init; } = default!;
    public DateTime AssignedAt { get; init; }
    public string AssignedBy   { get; init; } = "";
}

