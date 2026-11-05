using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using DriverLens.Core;

namespace DriverLens.Scanner;

public sealed class SetupApiDeviceScanner : IDeviceScanner
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;

    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_COMPATIBLEIDS = 0x00000002;
    private const uint SPDRP_CLASS = 0x00000007;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    private const uint DICS_FLAG_GLOBAL = 0x00000001;
    private const uint DIREG_DRV = 0x00000002;
    private const uint KEY_READ = 0x20019;

    private const uint DN_HAS_PROBLEM = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        [MarshalAs(UnmanagedType.LPTStr)] string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(
        IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        [Out] char[]? deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint scope,
        uint hwProfile,
        uint keyType,
        uint access);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
        uint ulFlags);

    public Task<IReadOnlyList<DeviceInfo>> ScanAsync()
    {
        return Task.Run<IReadOnlyList<DeviceInfo>>(() =>
        {
            var devices = new List<DeviceInfo>();
            var invalidHandle = new IntPtr(-1);
            var devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);

            if (devInfoSet == invalidHandle || devInfoSet == IntPtr.Zero)
            {
                return devices;
            }

            try
            {
                uint memberIndex = 0;
                while (true)
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

                    if (!SetupDiEnumDeviceInfo(devInfoSet, memberIndex, ref devInfoData))
                    {
                        break;
                    }

                    memberIndex++;

                    var instanceId = GetDeviceInstanceId(devInfoSet, ref devInfoData);
                    var friendlyName = GetStringProperty(devInfoSet, ref devInfoData, SPDRP_FRIENDLYNAME);
                    var deviceDesc = GetStringProperty(devInfoSet, ref devInfoData, SPDRP_DEVICEDESC);
                    var deviceClass = GetStringProperty(devInfoSet, ref devInfoData, SPDRP_CLASS) ?? "Unknown";

                    var displayName = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName : deviceDesc;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = "Unknown Device";
                    }

                    var hwids = GetMultiStringProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                    var compatibleIds = GetMultiStringProperty(devInfoSet, ref devInfoData, SPDRP_COMPATIBLEIDS);

                    // Driver info
                    string? currentDriverVersion = null;
                    DateOnly? currentDriverDate = null;
                    string? currentProvider = null;
                    string? currentInfName = null;

                    var hKey = SetupDiOpenDevRegKey(devInfoSet, ref devInfoData, DICS_FLAG_GLOBAL, 0, DIREG_DRV, KEY_READ);
                    if (hKey != IntPtr.Zero && hKey != invalidHandle)
                    {
                        var safeHandle = new SafeRegistryHandle(hKey, ownsHandle: true);
                        using (var regKey = RegistryKey.FromHandle(safeHandle))
                        {
                            currentDriverVersion = regKey.GetValue("DriverVersion") as string;
                            currentProvider = regKey.GetValue("ProviderName") as string;
                            currentInfName = regKey.GetValue("InfPath") as string;
                            var dateStr = regKey.GetValue("DriverDate") as string;

                            if (!string.IsNullOrEmpty(dateStr))
                            {
                                if (DateOnly.TryParseExact(dateStr, new[] { "MM-dd-yyyy", "M-d-yyyy", "yyyy-MM-dd" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                                {
                                    currentDriverDate = parsedDate;
                                }
                                else if (DateOnly.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDateFallback))
                                {
                                    currentDriverDate = parsedDateFallback;
                                }
                            }
                        }
                    }

                    // Problem Status
                    bool hasProblem = false;
                    int? problemCode = null;
                    if (CM_Get_DevNode_Status(out var status, out var problem, devInfoData.DevInst, 0) == 0)
                    {
                        hasProblem = (status & DN_HAS_PROBLEM) != 0;
                        problemCode = hasProblem ? (int)problem : null;
                    }

                    devices.Add(new DeviceInfo
                    {
                        DeviceInstanceId = instanceId,
                        FriendlyName = displayName,
                        DeviceClass = deviceClass,
                        HardwareIds = hwids,
                        CompatibleIds = compatibleIds,
                        CurrentDriverVersion = currentDriverVersion,
                        CurrentDriverDate = currentDriverDate,
                        CurrentProvider = currentProvider,
                        CurrentInfName = currentInfName,
                        HasProblem = hasProblem,
                        ProblemCode = problemCode
                    });
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return devices;
        });
    }

    private static string GetDeviceInstanceId(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData)
    {
        uint requiredSize = 0;
        SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, null, 0, out requiredSize);
        if (requiredSize == 0) return string.Empty;

        var buffer = new char[requiredSize];
        if (SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, buffer, (uint)buffer.Length, out _))
        {
            return new string(buffer).TrimEnd('\0');
        }
        return string.Empty;
    }

    private static string? GetStringProperty(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        uint requiredSize = 0;
        SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out _, null, 0, out requiredSize);
        if (requiredSize == 0) return null;

        var buffer = new byte[requiredSize];
        if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out _, buffer, requiredSize, out _))
        {
            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }
        return null;
    }

    private static string[] GetMultiStringProperty(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        uint requiredSize = 0;
        SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out _, null, 0, out requiredSize);
        if (requiredSize == 0) return Array.Empty<string>();

        var buffer = new byte[requiredSize];
        if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out _, buffer, requiredSize, out _))
        {
            var str = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            return str.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return Array.Empty<string>();
    }
}
