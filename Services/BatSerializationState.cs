using System;
using System.Threading;

namespace GestionAtelier.Services;

// ======================================================
// BAT SERIALIZATION STATE — Prevent concurrent BAT processing
// ======================================================
// In-memory state + 30-second timeout safety
public static class BatSerializationState
{
    private static readonly object _lock = new();
    private static bool _inProgress = false;
    private static string? _currentFileName = null;
    private static DateTime _startedAt = DateTime.MinValue;
    private const int TimeoutSeconds = 180; // PrismaPrepare can take 60-120s; 180s provides a safe margin

    // Current workflow step (updated at key moments in HandleEpreuve / copy-for-bat)
    private static string _currentStep = "";

    // Correlation ID for the current BAT (set in TryAcquire, cleared in Release)
    private static string? _correlationId = null;

    // Last completed BAT info (updated by HandleEpreuve on success)
    private static string? _lastCompletedFileName = null;
    private static DateTime? _lastCompletedAt = null;
    private static string? _lastPrismaLog = null;

    public static (bool inProgress, string? currentFileName, DateTime startedAt, string currentStep, string? correlationId) Get()
    {
        lock (_lock)
        {
            // Auto-reset if timed out
            if (_inProgress && (DateTime.UtcNow - _startedAt).TotalSeconds >= TimeoutSeconds)
            {
                Console.WriteLine($"[BAT][WARN] BAT serialization timeout — auto-reset (was: {_currentFileName})");
                _inProgress = false;
                _currentFileName = null;
                _startedAt = DateTime.MinValue;
                _currentStep = "";
                _correlationId = null;
            }
            return (_inProgress, _currentFileName, _startedAt, _currentStep, _correlationId);
        }
    }

    public static void SetStep(string step)
    {
        lock (_lock) { _currentStep = step; }
    }

    public static (string? lastCompletedFileName, DateTime? lastCompletedAt, string? lastPrismaLog) GetLastCompleted()
    {
        lock (_lock)
        {
            return (_lastCompletedFileName, _lastCompletedAt, _lastPrismaLog);
        }
    }

    public static void SetLastCompleted(string fileName, string prismaLog)
    {
        lock (_lock)
        {
            _lastCompletedFileName = fileName;
            _lastCompletedAt = DateTime.UtcNow;
            _lastPrismaLog = prismaLog;
        }
    }

    public static bool TryAcquire(string fileName, string correlationId = "")
    {
        lock (_lock)
        {
            // Auto-reset if timed out
            if (_inProgress && (DateTime.UtcNow - _startedAt).TotalSeconds >= TimeoutSeconds)
            {
                Console.WriteLine($"[BAT][WARN] BAT serialization timeout — auto-reset (was: {_currentFileName})");
                _inProgress = false;
                _currentFileName = null;
                _startedAt = DateTime.MinValue;
                _currentStep = "";
                _correlationId = null;
            }
            if (_inProgress) return false;
            _inProgress = true;
            _currentFileName = fileName;
            _startedAt = DateTime.UtcNow;
            _currentStep = "";
            _correlationId = string.IsNullOrEmpty(correlationId) ? null : correlationId;
            return true;
        }
    }

    public static void Release()
    {
        lock (_lock)
        {
            _inProgress = false;
            _currentFileName = null;
            _startedAt = DateTime.MinValue;
            _currentStep = "";
            _correlationId = null;
        }
    }
}
