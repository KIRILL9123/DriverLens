using System;
using System.Collections.Generic;
using System.Linq;

namespace DriverLens.Core;

public sealed class RankedMatchingEngine : IMatchingEngine
{
    private readonly int _currentOsBuild;
    private readonly string _currentArchitecture;

    public RankedMatchingEngine() : this(
        Environment.OSVersion.Version.Build,
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
    {
    }

    public RankedMatchingEngine(int currentOsBuild, string currentArchitecture)
    {
        _currentOsBuild = currentOsBuild;
        _currentArchitecture = currentArchitecture.Trim().ToLowerInvariant();
    }

    public MatchResult Match(DeviceInfo device, IEnumerable<DriverCandidate> candidates)
    {
        var result = new MatchResult
        {
            Device = device,
            SelectedCandidate = null,
            MatchTier = MatchTier.None,
            Status = MatchStatus.NotInIndex,
            Reason = "No matching drivers found in index."
        };

        // 1. Tier selection
        var candidateList = candidates.ToList();

        // Check Hardware ID intersection (case-insensitive)
        var exactPool = candidateList.Where(c =>
            c.Hwids.Any(h => device.HardwareIds.Any(dh => string.Equals(dh, h, StringComparison.OrdinalIgnoreCase)))
        ).ToList();

        List<DriverCandidate> pool;
        MatchTier tier;

        if (exactPool.Count > 0)
        {
            pool = exactPool;
            tier = MatchTier.ExactHardwareId;
        }
        else
        {
            // Check Compatible ID intersection
            var compPool = candidateList.Where(c =>
                c.CompatibleIds.Any(h => device.CompatibleIds.Any(dc => string.Equals(dc, h, StringComparison.OrdinalIgnoreCase)))
            ).ToList();

            if (compPool.Count > 0)
            {
                pool = compPool;
                tier = MatchTier.CompatibleId;
            }
            else
            {
                // Not in index at all
                result.MatchTier = MatchTier.None;
                result.Status = MatchStatus.NotInIndex;
                result.Reason = "No matching drivers found in index.";
                return result;
            }
        }

        result.MatchTier = tier;

        // 2. OS filter & 3. Signed filter
        var filteredPool = pool.Where(c =>
            c.MinOsBuild <= _currentOsBuild &&
            c.SupportedArch.Any(a => string.Equals(a, _currentArchitecture, StringComparison.OrdinalIgnoreCase)) &&
            c.AuthenticodePublisher != null
        ).ToList();

        // 4. Check if empty after filters
        if (filteredPool.Count == 0)
        {
            result.Status = MatchStatus.NoSafeCandidate;
            result.Reason = $"Matching candidates found ({tier}), but none passed safety and OS requirements (unsigned or architecture/OS build mismatch).";
            return result;
        }

        // 5. Ranking
        var sortedPool = filteredPool.OrderByDescending(c => c.KnownGood)
            .ThenByDescending(c => c, new DriverVersionComparer())
            .ToList();

        var selected = sortedPool.First();
        result.SelectedCandidate = selected;

        // 6. Status determination
        bool isNewer = false;
        if (device.CurrentDriverVersion == null)
        {
            isNewer = true;
        }
        else
        {
            isNewer = CompareVersions(selected.Version, device.CurrentDriverVersion) > 0;
        }

        if (isNewer)
        {
            result.Status = MatchStatus.UpdateAvailable;
            result.Reason = $"Newer signed driver from {selected.Provider} ({selected.Version}, {selected.ReleaseDate:yyyy-MM-dd}) available for {FormatTier(tier)} match.";
        }
        else
        {
            result.Status = MatchStatus.UpToDate;
            result.Reason = $"The system already has a matching or newer driver ({device.CurrentDriverVersion}) than index candidate ({selected.Version}).";
        }

        // 7. Append critical device class warning if applicable
        var lowerClass = device.DeviceClass.ToLowerInvariant();
        bool isCriticalClass = new[] { "net", "display", "media", "scsiadapter", "diskdrive" }.Contains(lowerClass)
            || lowerClass.Contains("storage");

        if (isCriticalClass)
        {
            result.Reason += " Critical device class, review before applying.";
        }

        return result;
    }

    private static string FormatTier(MatchTier tier)
    {
        return tier switch
        {
            MatchTier.ExactHardwareId => "exact hardware",
            MatchTier.CompatibleId => "compatible ID",
            _ => "generic"
        };
    }

    private static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var ver1) && Version.TryParse(v2, out var ver2))
        {
            return ver1.CompareTo(ver2);
        }
        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DriverVersionComparer : IComparer<DriverCandidate>
    {
        public int Compare(DriverCandidate? x, DriverCandidate? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            return CompareVersions(x.Version, y.Version);
        }
    }
}
