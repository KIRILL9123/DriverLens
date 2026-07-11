using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DriverLens.Core;

public sealed class JsonFixtureIndexRepository : IIndexRepository
{
    private readonly string _filePath;

    public JsonFixtureIndexRepository()
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-index.json");
    }

    public JsonFixtureIndexRepository(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<IReadOnlyList<DriverCandidate>> LoadCandidatesAsync()
    {
        if (!File.Exists(_filePath))
        {
            throw new FileNotFoundException($"Fixture index file not found at: {_filePath}");
        }

        using var stream = File.OpenRead(_filePath);
        var root = await JsonSerializer.DeserializeAsync<IndexRoot>(stream);
        if (root == null)
        {
            return Array.Empty<DriverCandidate>();
        }

        var candidates = new List<DriverCandidate>();
        foreach (var entry in root.Entries)
        {
            DateOnly releaseDate;
            if (!DateOnly.TryParse(entry.ReleaseDate, out releaseDate))
            {
                releaseDate = DateOnly.FromDateTime(DateTime.MinValue);
            }

            if (!Enum.TryParse<RiskLevel>(entry.RiskLevel, true, out var risk))
            {
                risk = RiskLevel.Low;
            }

            candidates.Add(new DriverCandidate
            {
                Id = entry.Id,
                Hwids = entry.Hwids,
                CompatibleIds = entry.CompatibleIds,
                Provider = entry.Provider,
                Version = entry.Version,
                ReleaseDate = releaseDate,
                MinOsBuild = entry.Os.MinBuild,
                SupportedArch = entry.Os.Arch,
                SourceUrl = entry.Source.Url,
                Sha256 = entry.Source.Sha256,
                AuthenticodePublisher = entry.Source.AuthenticodePublisher,
                RiskLevel = risk,
                KnownGood = entry.KnownGood
            });
        }

        return candidates;
    }

    private sealed class IndexRoot
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("entries")]
        public List<DriverCandidateDto> Entries { get; set; } = new();
    }

    private sealed class DriverCandidateDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("hwids")]
        public string[] Hwids { get; set; } = Array.Empty<string>();

        [JsonPropertyName("compatible_ids")]
        public string[] CompatibleIds { get; set; } = Array.Empty<string>();

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("os")]
        public OsRequirementsDto Os { get; set; } = new();

        [JsonPropertyName("source")]
        public SourceDto Source { get; set; } = new();

        [JsonPropertyName("risk_level")]
        public string RiskLevel { get; set; } = string.Empty;

        [JsonPropertyName("known_good")]
        public bool KnownGood { get; set; }
    }

    private sealed class OsRequirementsDto
    {
        [JsonPropertyName("min_build")]
        public int MinBuild { get; set; }

        [JsonPropertyName("arch")]
        public string[] Arch { get; set; } = Array.Empty<string>();
    }

    private sealed class SourceDto
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("authenticode_publisher")]
        public string? AuthenticodePublisher { get; set; }
    }
}
