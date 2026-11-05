using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DriverLens.Core;

public interface ILocalCacheStore
{
    Task ReplaceAllCandidatesAsync(IEnumerable<DriverCandidate> candidates);
    Task<IReadOnlyList<DriverCandidate>> GetCachedCandidatesAsync();
    Task<(string? ETag, DateTime LastSyncedUtc)> GetSyncMetaAsync(string shardKey);
    Task SetSyncMetaAsync(string shardKey, string? etag, DateTime syncedUtc);
}
