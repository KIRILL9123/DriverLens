using System.Collections.Generic;
using System.Threading.Tasks;

namespace DriverLens.Core;

public interface IDeviceScanner
{
    Task<IReadOnlyList<DeviceInfo>> ScanAsync();
}
