using System.Threading.Tasks;

namespace DriverLens.Core;

public interface IRestorePointService
{
    Task<bool> CreateRestorePointAsync();
    Task<bool> IsSystemRestoreEnabledHeuristicAsync();
}
