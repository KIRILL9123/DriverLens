using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using DriverLens.Core;
using DriverLens.Data;

namespace DriverLens.Tests;

public sealed class GithubIndexSyncServiceTests
{
    private static readonly byte[] TestPublicKey;
    private static readonly byte[] TestPrivateKey;

    static GithubIndexSyncServiceTests()
    {
        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            TestPublicKey = ecdsa.ExportSubjectPublicKeyInfo();
            TestPrivateKey = ecdsa.ExportECPrivateKey();
        }
    }

    [Fact]
    public async Task Sync_returns_NotModified_and_does_not_wipe_cache_on_304()
    {
        var cache = new MockCacheStore { ETag = "etag-123" };
        var httpHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                Assert.Equal("etag-123", req.Headers.IfNoneMatch.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
        };

        var httpClient = new HttpClient(httpHandler);
        var syncService = new GithubIndexSyncService(cache, httpClient, TestPublicKey);

        var result = await syncService.SyncAsync();

        Assert.Equal(SyncResult.NotModified, result);
        Assert.False(cache.ReplaceAllCalled);
    }

    [Fact]
    public async Task Sync_returns_Updated_and_replaces_cache_on_valid_200()
    {
        var cache = new MockCacheStore { ETag = null };

        // Setup valid payload & signature
        var payload = "{\"schema_version\": 1, \"entries\": [{" +
            "\"id\": \"test-candidate\"," +
            "\"hwids\": [\"PCI\\\\VEN_1111\"]," +
            "\"compatible_ids\": []," +
            "\"provider\": \"TestVendor\"," +
            "\"version\": \"1.0.0.0\"," +
            "\"release_date\": \"2026-07-01\"," +
            "\"os\": { \"min_build\": 19041, \"arch\": [\"x64\"] }," +
            "\"source\": { \"url\": \"http://example.com/drv.cab\", \"sha256\": \"abc\", \"authenticode_publisher\": \"Vendor Sign\" }," +
            "\"risk_level\": \"low\"," +
            "\"known_good\": true" +
            "}]}";

        var signature = SignPayload(payload, TestPrivateKey);

        var signedShardJson = JsonSerializer.Serialize(new
        {
            algorithm = "ECDSA-P256-SHA256",
            payload = payload,
            signature = signature
        });

        var httpHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(signedShardJson, Encoding.UTF8, "application/json")
                };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"new-etag-456\"");
                return Task.FromResult(response);
            }
        };

        var httpClient = new HttpClient(httpHandler);
        var syncService = new GithubIndexSyncService(cache, httpClient, TestPublicKey);

        var result = await syncService.SyncAsync();

        Assert.Equal(SyncResult.Updated, result);
        Assert.True(cache.ReplaceAllCalled);
        Assert.NotNull(cache.ReplacedCandidates);
        var list = new List<DriverCandidate>(cache.ReplacedCandidates);
        Assert.Single(list);
        Assert.Equal("test-candidate", list[0].Id);
        Assert.Equal("\"new-etag-456\"", cache.ETag);
        Assert.True(cache.LastSyncedUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Sync_returns_VerificationFailed_and_leaves_cache_untouched_on_tampered_200()
    {
        var cache = new MockCacheStore { ETag = null };

        var payload = "{\"schema_version\": 1, \"entries\": []}";
        var signature = SignPayload(payload, TestPrivateKey);

        // Tamper with the payload string in signed shard json
        var signedShardJson = JsonSerializer.Serialize(new
        {
            algorithm = "ECDSA-P256-SHA256",
            payload = payload + "tampered", // Modifying the payload to invalidate signature
            signature = signature
        });

        var httpHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(signedShardJson, Encoding.UTF8, "application/json")
            })
        };

        var httpClient = new HttpClient(httpHandler);
        var syncService = new GithubIndexSyncService(cache, httpClient, TestPublicKey);

        var result = await syncService.SyncAsync();

        Assert.Equal(SyncResult.VerificationFailed, result);
        Assert.False(cache.ReplaceAllCalled);
    }

    [Fact]
    public async Task Sync_returns_NetworkUnavailable_and_does_not_throw_on_http_exception()
    {
        var cache = new MockCacheStore { ETag = "some-etag" };
        var httpHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req => throw new HttpRequestException("Simulated connection failure")
        };

        var httpClient = new HttpClient(httpHandler);
        var syncService = new GithubIndexSyncService(cache, httpClient, TestPublicKey);

        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await syncService.SyncAsync();
            Assert.Equal(SyncResult.NetworkUnavailable, result);
        });

        Assert.Null(exception);
        Assert.False(cache.ReplaceAllCalled);
    }

    private static string SignPayload(string payload, byte[] privateKeyBytes)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(privateKeyBytes, out _);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signatureBytes);
    }

    private sealed class MockCacheStore : ILocalCacheStore
    {
        public bool ReplaceAllCalled { get; private set; }
        public IEnumerable<DriverCandidate>? ReplacedCandidates { get; private set; }

        public string? ETag { get; set; }
        public DateTime LastSyncedUtc { get; set; } = DateTime.MinValue;

        public Task ReplaceAllCandidatesAsync(IEnumerable<DriverCandidate> candidates)
        {
            ReplaceAllCalled = true;
            ReplacedCandidates = candidates;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverCandidate>> GetCachedCandidatesAsync()
        {
            return Task.FromResult<IReadOnlyList<DriverCandidate>>(new List<DriverCandidate>());
        }

        public Task<(string? ETag, DateTime LastSyncedUtc)> GetSyncMetaAsync(string shardKey)
        {
            return Task.FromResult((ETag, LastSyncedUtc));
        }

        public Task SetSyncMetaAsync(string shardKey, string? etag, DateTime syncedUtc)
        {
            ETag = etag;
            LastSyncedUtc = syncedUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> HandlerFunc { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return HandlerFunc(request);
        }
    }
}
