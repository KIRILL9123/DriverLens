using System.Collections.Generic;

namespace DriverLens.App;

public sealed class DeviceGroupViewModel
{
    public string DeviceClass { get; }
    public IReadOnlyList<DeviceItemViewModel> Devices { get; }

    public DeviceGroupViewModel(string deviceClass, IReadOnlyList<DeviceItemViewModel> devices)
    {
        DeviceClass = deviceClass;
        Devices = devices;
    }
}
