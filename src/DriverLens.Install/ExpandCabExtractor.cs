using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DriverLens.Core;

namespace DriverLens.Install;

public sealed class ExpandCabExtractor : ICabExtractor
{
    public async Task<string> ExtractAsync(string cabPath, string destDir)
    {
        if (!string.Equals(Path.GetExtension(cabPath), ".cab", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Unsupported package format: only CAB archives are supported.");
        }

        Directory.CreateDirectory(destDir);

        var psi = new ProcessStartInfo
        {
            FileName = "expand.exe",
            Arguments = $"-F:* \"{cabPath}\" \"{destDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start expand.exe process.");
        }

        var stdoutBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        var stdout = stdoutBuilder.ToString();
        var stderr = stderrBuilder.ToString();
        
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"expand.exe failed with exit code {process.ExitCode}. Error: {stderr}");
        }

        // Search destDir for INF files: top-level first
        var topLevelInfs = Directory.GetFiles(destDir, "*.inf", SearchOption.TopDirectoryOnly);
        string[] allInfs = topLevelInfs;

        if (allInfs.Length == 0)
        {
            // If none found top-level, search one level of subdirectories
            var subDirs = Directory.GetDirectories(destDir);
            var subLevelInfs = subDirs.SelectMany(d => Directory.GetFiles(d, "*.inf", SearchOption.TopDirectoryOnly)).ToArray();
            allInfs = subLevelInfs;
        }

        if (allInfs.Length == 1)
        {
            return allInfs[0];
        }

        throw new InvalidOperationException($"Unsupported package structure — expected exactly one INF, found {allInfs.Length}");
    }
}
