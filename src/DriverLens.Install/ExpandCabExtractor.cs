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

        // Read stdout and stderr asynchronously in the background to prevent stream buffer deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        
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
