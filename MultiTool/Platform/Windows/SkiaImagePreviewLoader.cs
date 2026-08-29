using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MultiTool.Modules.BatchFileOrganizer;
using SkiaSharp;

namespace MultiTool.Platform.Windows;

public sealed class SkiaImagePreviewLoader : IImagePreviewLoader
{
    public async Task<ImageSource> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var encodedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return await Task.Run(
            () => Decode(encodedBytes, cancellationToken),
            cancellationToken);
    }

    private static BitmapSource Decode(byte[] encodedBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = SKBitmap.Decode(encodedBytes)
            ?? throw new InvalidDataException("The selected image could not be decoded.");
        if (decoded.Width <= 0 || decoded.Height <= 0)
        {
            throw new InvalidDataException("The selected image has invalid dimensions.");
        }

        var imageInfo = new SKImageInfo(
            decoded.Width,
            decoded.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var normalized = new SKBitmap(imageInfo);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                decoded,
                0,
                0,
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                null);
            canvas.Flush();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pixels = new byte[normalized.ByteCount];
        Marshal.Copy(normalized.GetPixels(), pixels, 0, pixels.Length);
        var bitmap = BitmapSource.Create(
            normalized.Width,
            normalized.Height,
            96,
            96,
            PixelFormats.Pbgra32,
            null,
            pixels,
            normalized.RowBytes);
        bitmap.Freeze();
        return bitmap;
    }
}
