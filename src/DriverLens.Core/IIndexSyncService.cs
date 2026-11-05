using System.Threading.Tasks;

namespace DriverLens.Core;

public enum SyncResult
{
    NotModified,
    Updated,
    VerificationFailed,
    NetworkUnavailable
}

public interface IIndexSyncService
{
    Task<SyncResult> SyncAsync();
}
