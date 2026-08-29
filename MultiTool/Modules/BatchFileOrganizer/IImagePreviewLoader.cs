using System.Windows.Media;

namespace MultiTool.Modules.BatchFileOrganizer;

public interface IImagePreviewLoader
{
    Task<ImageSource> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
