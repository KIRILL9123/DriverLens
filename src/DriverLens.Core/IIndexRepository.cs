using System.Collections.Generic;
using System.Threading.Tasks;

namespace DriverLens.Core;

public interface IIndexRepository
{
    Task<IReadOnlyList<DriverCandidate>> LoadCandidatesAsync();
}
