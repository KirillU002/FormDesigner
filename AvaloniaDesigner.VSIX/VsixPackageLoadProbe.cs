using System;
using System.IO;

namespace AvaloniaDesigner.VSIX;

/// <summary>
/// Temporary PoC marker for distinguishing package registration failures from load failures.
/// It deliberately avoids Visual Studio services and never throws into devenv.exe.
/// </summary>
internal static class VsixPackageLoadProbe
{
    private const string FileName = "AvaloniaDesignerVsix-package-load.log";

    public static void Write(string stage, Exception? exception = null)
    {
        try
        {
            var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
            var line = $"{DateTimeOffset.UtcNow:O} {stage}{details}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(Path.GetTempPath(), FileName), line);
        }
        catch
        {
            // The probe is diagnostic-only and must not alter package load behavior.
        }
    }
}
