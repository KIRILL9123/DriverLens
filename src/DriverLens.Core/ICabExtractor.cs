using System.Threading.Tasks;

namespace DriverLens.Core;

public interface ICabExtractor
{
    Task<string> ExtractAsync(string cabPath, string destDir);
}
