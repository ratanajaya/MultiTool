using System.Windows.Media.Imaging;
using MultiTool.Platform.Windows;
using SkiaSharp;

namespace MultiTool.Tests.Modules.BatchFileOrganizer;

[TestClass]
public sealed class SkiaImagePreviewLoaderTests
{
    [TestMethod]
    public async Task Load_WebPDecodesAndReleasesTheFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MultiTool.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "preview.webp");
        Directory.CreateDirectory(folder);
        try
        {
            using var bitmap = new SKBitmap(2, 3);
            bitmap.Erase(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 100);
            File.WriteAllBytes(path, encoded.ToArray());

            var source = await new SkiaImagePreviewLoader().LoadAsync(path);

            var bitmapSource = (BitmapSource)source;
            Assert.AreEqual(2, bitmapSource.PixelWidth);
            Assert.AreEqual(3, bitmapSource.PixelHeight);
            Assert.IsTrue(bitmapSource.IsFrozen);
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
