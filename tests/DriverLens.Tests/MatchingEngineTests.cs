using System;
using System.Collections.Generic;
using Xunit;
using DriverLens.Core;

namespace DriverLens.Tests;

public sealed class MatchingEngineTests
{
    private readonly DriverCandidate _exactCandidate1;
    private readonly DriverCandidate _exactCandidate2Unsigned;
    private readonly DriverCandidate _exactCandidate3Regressed;
    private readonly DriverCandidate _compatibleCandidate;
    private readonly List<DriverCandidate> _candidates;

    public MatchingEngineTests()
    {
        _exactCandidate1 = new DriverCandidate
        {
            Id = "exact-signed-good-3.2.1.0",
            Hwids = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02", "PCI\\VEN_1414&DEV_00B7" },
            CompatibleIds = new string[0],
            Provider = "Contoso",
            Version = "3.2.1.0",
            ReleaseDate = new DateOnly(2026, 4, 10),
            MinOsBuild = 19041,
            SupportedArch = new[] { "x64" },
            SourceUrl = "https://drivers.contoso.example/net/3.2.1.0.cab",
            Sha256 = "3b1e9f4a2c7d0e8b5a6f1c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f",
            AuthenticodePublisher = "Contoso Networking Inc",
            RiskLevel = RiskLevel.Low,
            KnownGood = true
        };

        _exactCandidate2Unsigned = new DriverCandidate
        {
            Id = "exact-unsigned-3.3.0.0",
            Hwids = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02", "PCI\\VEN_1414&DEV_00B7" },
            CompatibleIds = new string[0],
            Provider = "Contoso",
            Version = "3.3.0.0",
            ReleaseDate = new DateOnly(2026, 5, 1),
            MinOsBuild = 19041,
            SupportedArch = new[] { "x64" },
            SourceUrl = "https://drivers.contoso.example/net/3.3.0.0.cab",
            Sha256 = "hash2",
            AuthenticodePublisher = null, // Unsigned!
            RiskLevel = RiskLevel.High,
            KnownGood = true
        };

        _exactCandidate3Regressed = new DriverCandidate
        {
            Id = "exact-signed-regressed-3.5.0.0",
            Hwids = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02", "PCI\\VEN_1414&DEV_00B7" },
            CompatibleIds = new string[0],
            Provider = "Contoso",
            Version = "3.5.0.0",
            ReleaseDate = new DateOnly(2026, 6, 1),
            MinOsBuild = 19041,
            SupportedArch = new[] { "x64" },
            SourceUrl = "https://drivers.contoso.example/net/3.5.0.0.cab",
            Sha256 = "hash3",
            AuthenticodePublisher = "Contoso Networking Inc",
            RiskLevel = RiskLevel.Medium,
            KnownGood = false // Regressed!
        };

        _compatibleCandidate = new DriverCandidate
        {
            Id = "comp-signed-good-2.0.0.0",
            Hwids = new string[0],
            CompatibleIds = new[] { "PCI\\VEN_1414&DEV_00B7&CC_020000" },
            Provider = "Contoso Generic",
            Version = "2.0.0.0",
            ReleaseDate = new DateOnly(2025, 1, 1),
            MinOsBuild = 10240,
            SupportedArch = new[] { "x64", "x86" },
            SourceUrl = "https://drivers.contoso.example/net/generic.cab",
            Sha256 = "hash4",
            AuthenticodePublisher = "Contoso Networking Inc",
            RiskLevel = RiskLevel.Low,
            KnownGood = true
        };

        _candidates = new List<DriverCandidate>
        {
            _exactCandidate1,
            _exactCandidate2Unsigned,
            _exactCandidate3Regressed,
            _compatibleCandidate
        };
    }

    [Fact]
    public void Exact_hardware_id_preferred_over_compatible_id()
    {
        // Device matches both exact hwid and compatible id
        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_01",
            FriendlyName = "Test Network Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02" },
            CompatibleIds = new[] { "PCI\\VEN_1414&DEV_00B7&CC_020000" },
            CurrentDriverVersion = "1.0.0.0",
            CurrentDriverDate = new DateOnly(2022, 1, 1),
            CurrentProvider = "Microsoft"
        };

        var engine = new RankedMatchingEngine(19041, "x64");
        var result = engine.Match(device, _candidates);

        Assert.Equal(MatchTier.ExactHardwareId, result.MatchTier);
        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("exact-signed-good-3.2.1.0", result.SelectedCandidate.Id);
    }

    [Fact]
    public void Compatible_id_used_as_fallback_if_no_exact_match()
    {
        // Device does NOT match exact hwid, but matches compatible id
        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_02",
            FriendlyName = "Generic Net Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_9999" }, // No exact match
            CompatibleIds = new[] { "PCI\\VEN_1414&DEV_00B7&CC_020000" },
            CurrentDriverVersion = "1.0.0.0"
        };

        var engine = new RankedMatchingEngine(19041, "x64");
        var result = engine.Match(device, _candidates);

        Assert.Equal(MatchTier.CompatibleId, result.MatchTier);
        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("comp-signed-good-2.0.0.0", result.SelectedCandidate.Id);
    }

    [Fact]
    public void Unsigned_candidates_are_never_selected()
    {
        // exactCandidate2Unsigned has a higher version (3.3.0.0) than exactCandidate1 (3.2.1.0),
        // but it is unsigned (AuthenticodePublisher = null)
        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_01",
            FriendlyName = "Test Network Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02" },
            CompatibleIds = new string[0],
            CurrentDriverVersion = "1.0.0.0"
        };

        var engine = new RankedMatchingEngine(19041, "x64");
        var result = engine.Match(device, _candidates);

        Assert.Equal(MatchTier.ExactHardwareId, result.MatchTier);
        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("exact-signed-good-3.2.1.0", result.SelectedCandidate.Id); // Skips 3.3.0.0 unsigned
    }

    [Fact]
    public void Candidate_exceeding_min_os_build_is_dropped()
    {
        // Let's create an OS environment with build 10240.
        // exactCandidate1 requires MinOsBuild 19041. compatibleCandidate requires 10240.
        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_01",
            FriendlyName = "Test Network Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02" },
            CompatibleIds = new[] { "PCI\\VEN_1414&DEV_00B7&CC_020000" },
            CurrentDriverVersion = "1.0.0.0"
        };

        // Match on Windows 10 Build 10240 (original Windows 10)
        var engine = new RankedMatchingEngine(10240, "x64");
        var result = engine.Match(device, _candidates);

        // Exact pool exists (exactCandidate1, 2, 3). But they all require min_build 19041!
        // So the exact pool is empty after OS filtering.
        // The algorithm stops at step 4: pool was non-empty after step 1, but empty after steps 2-3.
        // Result is NoSafeCandidate, MatchTier = ExactHardwareId.
        Assert.Equal(MatchStatus.NoSafeCandidate, result.Status);
        Assert.Null(result.SelectedCandidate);
        Assert.Equal(MatchTier.ExactHardwareId, result.MatchTier);
    }

    [Fact]
    public void Known_good_loses_newer_version_if_newer_is_regressed()
    {
        // exactCandidate3Regressed is version 3.5.0.0 (newer than 3.2.1.0) but KnownGood is false.
        // exactCandidate1 is version 3.2.1.0 but KnownGood is true.
        // The engine sorts by KnownGood descending first, then version descending.
        // So 3.2.1.0 (known good) should win!
        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_01",
            FriendlyName = "Test Network Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02" },
            CompatibleIds = new string[0],
            CurrentDriverVersion = "1.0.0.0"
        };

        var engine = new RankedMatchingEngine(19041, "x64");
        var result = engine.Match(device, _candidates);

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("exact-signed-good-3.2.1.0", result.SelectedCandidate.Id);
    }

    [Fact]
    public void Correct_status_reported_in_scenarios()
    {
        var candidatesList = new List<DriverCandidate> { _exactCandidate1 };

        var device = new DeviceInfo
        {
            DeviceInstanceId = "DEV_01",
            FriendlyName = "Test Network Card",
            DeviceClass = "Net",
            HardwareIds = new[] { "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02" },
            CompatibleIds = new string[0]
        };

        var engine = new RankedMatchingEngine(19041, "x64");

        // Scenario 1: UpdateAvailable (Current is older, or no driver)
        device.CurrentDriverVersion = "1.0.0.0";
        var res1 = engine.Match(device, candidatesList);
        Assert.Equal(MatchStatus.UpdateAvailable, res1.Status);

        // Scenario 2: UpToDate (Current is same or newer)
        device.CurrentDriverVersion = "3.2.1.0";
        var res2 = engine.Match(device, candidatesList);
        Assert.Equal(MatchStatus.UpToDate, res2.Status);

        device.CurrentDriverVersion = "4.0.0.0";
        var res3 = engine.Match(device, candidatesList);
        Assert.Equal(MatchStatus.UpToDate, res3.Status);

        // Scenario 3: NotInIndex (Device not in index at all)
        var remoteDevice = new DeviceInfo
        {
            HardwareIds = new[] { "PCI\\VEN_8086&DEV_1234" }
        };
        var res4 = engine.Match(remoteDevice, candidatesList);
        Assert.Equal(MatchStatus.NotInIndex, res4.Status);
        Assert.Null(res4.SelectedCandidate);
    }
}
