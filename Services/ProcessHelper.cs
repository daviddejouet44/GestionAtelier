using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GestionAtelier.Services;

/// <summary>
/// Cross-platform helper to launch shell commands.
/// On Windows uses cmd.exe; on Linux/macOS uses /bin/bash.
/// </summary>
public static class ProcessHelper
{
    public static Process? StartShellCommand(string command)
    {
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = true
            };
        }
        else
        {
            // Escape any embedded double-quotes in the command string
            var escaped = command.Replace("\"", "\\\"");
            psi = new ProcessStartInfo("/bin/bash", $"-c \"{escaped}\"")
            {
                UseShellExecute = false
            };
        }
        return Process.Start(psi);
    }
}
