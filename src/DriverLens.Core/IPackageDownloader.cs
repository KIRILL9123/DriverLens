using System.Threading.Tasks;

namespace DriverLens.Core;

public interface IPackageDownloader
{
    Task<string> DownloadAsync(DriverCandidate candidate, string destDir);
}
