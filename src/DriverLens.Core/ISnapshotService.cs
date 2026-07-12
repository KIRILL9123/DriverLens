using System.Threading.Tasks;

namespace DriverLens.Core;

public interface ISnapshotService
{
    Task<string?> SnapshotAsync(DeviceInfo device, string destDir);
}
