using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DriverLens.Core;

namespace DriverLens.Data;

public sealed class GithubIndexSyncService : IIndexSyncService
{
    private const string ShardUrl = "https://raw.githubusercontent.com/KIRILL9123/DriverLens/main/index/shards/net.json.signed.json";
    
    private readonly ILocalCacheStore _cacheStore;
    private readonly HttpClient _httpClient;
    private readonly byte[]? _publicKey;

    public GithubIndexSyncService(ILocalCacheStore cacheStore, HttpClient? httpClient = null, byte[]? publicKey = null)
    {
        _cacheStore = cacheStore;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _publicKey = publicKey;
    }

    public async Task<SyncResult> SyncAsync()
    {
        try
        {
            // 1. Read cached ETag
            var (etag, _) = await _cacheStore.GetSyncMetaAsync("net");

            // 2. Prepare HTTP request
            var request = new HttpRequestMessage(HttpMethod.Get, ShardUrl);
            if (!string.IsNullOrEmpty(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            // 3. Send request
            var response = await _httpClient.SendAsync(request);

            // 4. Handle NotModified (304)
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return SyncResult.NotModified;
            }

            // 5. Handle OK (200)
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var jsonBody = await response.Content.ReadAsStringAsync();
                var signedShard = JsonSerializer.Deserialize<SignedShard>(jsonBody);

                if (signedShard == null || string.IsNullOrEmpty(signedShard.Payload) || string.IsNullOrEmpty(signedShard.Signature))
                {
                    System.Diagnostics.Debug.WriteLine("Warning: Shard structure is malformed.");
                    return SyncResult.VerificationFailed;
                }

                // Verify ECDSA signature
                bool isVerified = SignedIndexVerifier.Verify(signedShard.Payload, signedShard.Signature, _publicKey);
                if (!isVerified)
                {
                    System.Diagnostics.Debug.WriteLine("Warning: Signature verification failed for the downloaded index shard.");
                    return SyncResult.VerificationFailed;
                }

                // Deserialize and replace cache candidates
                var candidates = ParseCandidates(signedShard.Payload);
                await _cacheStore.ReplaceAllCandidatesAsync(candidates);

                // Get new ETag from response header
                string? newEtag = null;
                if (response.Headers.ETag != null)
                {
                    newEtag = response.Headers.ETag.ToString();
                }
                else if (response.Headers.TryGetValues("ETag", out var etagValues))
                {
                    newEtag = etagValues.FirstOrDefault();
                }

                await _cacheStore.SetSyncMetaAsync("net", newEtag, DateTime.UtcNow);

                return SyncResult.Updated;
            }

            // Other statuses (404, 500, etc.) are treated as network/resource unavailable
            System.Diagnostics.Debug.WriteLine($"Warning: Index sync endpoint returned HTTP status {response.StatusCode}");
            return SyncResult.NetworkUnavailable;
        }
        catch (Exception ex)
        {
            // Log exception at info/debug level, never let it propagate to UI
            System.Diagnostics.Debug.WriteLine($"Info: Sync failed due to network exception: {ex.Message}");
            return SyncResult.NetworkUnavailable;
        }
    }

    private static List<DriverCandidate> ParseCandidates(string payloadJson)
    {
        var root = JsonSerializer.Deserialize<IndexRootDto>(payloadJson);
        if (root == null) return new List<DriverCandidate>();

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

    private sealed class IndexRootDto
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
