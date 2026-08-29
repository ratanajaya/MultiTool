using System.Windows.Media;

namespace MultiTool.Modules.BatchFileOrganizer;

public interface IImagePreviewLoader
{
    Task<ImageSource> LoadAsync(
        string filePath,
        int? maximumPixelDimension = null,
        CancellationToken cancellationToken = default);
}
