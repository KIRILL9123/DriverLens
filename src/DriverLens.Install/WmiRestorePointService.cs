using System;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;
using DriverLens.Core;

namespace DriverLens.Install;

[SupportedOSPlatform("windows")]
public sealed class WmiRestorePointService : IRestorePointService
{
    public Task<bool> IsSystemRestoreEnabledHeuristicAsync()
    {
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                if (key == null)
                {
                    return false;
                }

                var val = key.GetValue("RPSessionInterval");
                if (val == null)
                {
                    return false;
                }

                if (val is int intVal && intVal == 0)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> CreateRestorePointAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var mc = new ManagementClass(@"\\.\root\default:SystemRestore");
                
                // InvokeMethod directly using the overload mentioned in the prompt
                var result = mc.InvokeMethod("CreateRestorePoint", new object[] { "DriverLens: before driver update", 0, 100 });
                
                if (result != null)
                {
                    // Return value of 0 means success
                    if (result is uint uintVal && uintVal == 0)
                    {
                        return true;
                    }
                    if (result is int intVal && intVal == 0)
                    {
                        return true;
                    }
                }
                
                // If it succeeded without returning a specific code, let's treat any non-exception
                // WMI call that returns 0 as success, but if it returns something else, it failed.
                // Let's also check if it returns null, which might mean success or void, but CreateRestorePoint returns uint.
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling CreateRestorePoint via WMI: {ex.Message}");
                return false;
            }
        });
    }
}
