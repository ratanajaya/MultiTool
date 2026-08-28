using System.IO;
using System.Text.Json;

namespace MultiTool.Core.Settings;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MultiTool",
            "settings.json");
    }

    public async Task<MultiToolSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new MultiToolSettings();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<MultiToolSettings>(
                   stream,
                   SerializerOptions,
                   cancellationToken)
               ?? new MultiToolSettings();
    }

    public async Task SaveAsync(MultiToolSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
