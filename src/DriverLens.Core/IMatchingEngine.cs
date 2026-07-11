using System.Collections.Generic;

namespace DriverLens.Core;

public interface IMatchingEngine
{
    MatchResult Match(DeviceInfo device, IEnumerable<DriverCandidate> candidates);
}
