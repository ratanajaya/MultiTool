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
        int? maximumPixelDimension = null,
        CancellationToken cancellationToken = default)
    {
        var encodedBytes = await ReadAllBytesWithoutLockingAsync(filePath, cancellationToken);
        return await Task.Run(
            () => Decode(encodedBytes, maximumPixelDimension, cancellationToken),
            cancellationToken);
    }

    private static async Task<byte[]> ReadAllBytesWithoutLockingAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("The selected image is too large to preview.");
        }

        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    private static BitmapSource Decode(
        byte[] encodedBytes,
        int? maximumPixelDimension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = SKBitmap.Decode(encodedBytes)
            ?? throw new InvalidDataException("The selected image could not be decoded.");
        if (decoded.Width <= 0 || decoded.Height <= 0)
        {
            throw new InvalidDataException("The selected image has invalid dimensions.");
        }

        var scale = maximumPixelDimension is > 0
            ? Math.Min(1d, (double)maximumPixelDimension.Value / Math.Max(decoded.Width, decoded.Height))
            : 1d;
        var outputWidth = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        var imageInfo = new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var normalized = new SKBitmap(imageInfo);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                decoded,
                new SKRect(0, 0, outputWidth, outputHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
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
