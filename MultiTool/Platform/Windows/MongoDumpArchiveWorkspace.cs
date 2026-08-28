using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace MultiTool.Platform.Windows;

internal sealed class MongoDumpArchiveWorkspace : IDisposable
{
    private readonly string _runDirectory;
    private readonly bool _overwriteExisting;
    private bool _completed;
    private bool _disposed;

    private MongoDumpArchiveWorkspace(
        string runDirectory,
        string configPath,
        string partialArchivePath,
        string finalArchivePath,
        bool overwriteExisting)
    {
        _runDirectory = runDirectory;
        ConfigPath = configPath;
        PartialArchivePath = partialArchivePath;
        FinalArchivePath = finalArchivePath;
        _overwriteExisting = overwriteExisting;
    }

    public string ConfigPath { get; }

    public string PartialArchivePath { get; }

    public string FinalArchivePath { get; }

    public static MongoDumpArchiveWorkspace Create(
        string temporaryRoot,
        string finalArchivePath,
        bool overwriteExisting,
        string connectionString)
    {
        Directory.CreateDirectory(temporaryRoot);
        var runDirectory = Path.Combine(temporaryRoot, $"run-{Guid.NewGuid():N}");
        CreateCurrentUserOnlyDirectory(runDirectory);

        var configPath = Path.Combine(runDirectory, "mongodump.yml");
        var partialArchivePath = $"{finalArchivePath}.{Guid.NewGuid():N}.partial";
        try
        {
            var yaml = $"uri: {JsonSerializer.Serialize(connectionString)}{Environment.NewLine}";
            File.WriteAllText(configPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new MongoDumpArchiveWorkspace(
                runDirectory,
                configPath,
                partialArchivePath,
                finalArchivePath,
                overwriteExisting);
        }
        catch
        {
            TryDeleteDirectory(runDirectory);
            throw;
        }
    }

    public long Complete()
    {
        if (!File.Exists(PartialArchivePath))
        {
            throw new IOException("mongodump exited successfully but did not create an archive.");
        }

        var size = new FileInfo(PartialArchivePath).Length;
        if (size == 0)
        {
            throw new IOException("mongodump created an empty archive.");
        }

        if (File.Exists(FinalArchivePath) && !_overwriteExisting)
        {
            throw new IOException("The destination archive was created by another process during the backup.");
        }

        File.Move(PartialArchivePath, FinalArchivePath, _overwriteExisting);
        _completed = true;
        return size;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? cleanupError = null;
        try
        {
            if (!_completed && File.Exists(PartialArchivePath))
            {
                File.Delete(PartialArchivePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cleanupError = exception;
        }

        try
        {
            if (Directory.Exists(_runDirectory))
            {
                Directory.Delete(_runDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cleanupError ??= exception;
        }

        if (cleanupError is not null)
        {
            throw new InvalidOperationException(
                "A protected MongoDB backup temporary file could not be removed.",
                cleanupError);
        }
    }

    public static Task CleanupStaleFilesAsync(
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(temporaryRoot))
        {
            return Task.CompletedTask;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot));
        foreach (var directory in Directory.EnumerateDirectories(normalizedRoot, "run-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedDirectory = Path.GetFullPath(directory);
            if (!string.Equals(
                    Path.GetDirectoryName(normalizedDirectory),
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.Delete(normalizedDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static void CreateCurrentUserOnlyDirectory(string path)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user could not be identified.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var directory = Directory.CreateDirectory(path);
        directory.SetAccessControl(security);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // The original creation failure remains the actionable error.
        }
    }
}
