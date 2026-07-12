using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DriverLens.Install;

public class PnpUtilWrapper
{
    public virtual async Task<string> ExportDriverAsync(string infName, string destDir)
    {
        Directory.CreateDirectory(destDir);

        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = $"/export-driver {infName} \"{destDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start pnputil.exe process.");
        }

        await process.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pnputil /export-driver failed with exit code {process.ExitCode}. Output: {stdout} {stderr}");
        }

        var dirs = Directory.GetDirectories(destDir);
        if (dirs.Length > 0)
        {
            return dirs[0];
        }

        return destDir;
    }

    public virtual async Task<(int ExitCode, string Output)> AddDriverAsync(string infPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = $"/add-driver \"{infPath}\" /install",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start pnputil.exe process for driver installation.");
        }

        await process.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        var combined = $"{stdout}\n{stderr}".Trim();

        return (process.ExitCode, combined);
    }
}
