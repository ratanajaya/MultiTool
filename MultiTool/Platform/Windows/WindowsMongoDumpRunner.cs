using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Platform.Windows;

public sealed partial class WindowsMongoDumpRunner : IMongoDumpRunner
{
    private static readonly Version MinimumVersion = new(100, 3, 0);
    private readonly string _temporaryRoot;

    public WindowsMongoDumpRunner(string? temporaryRoot = null)
    {
        _temporaryRoot = temporaryRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MultiTool",
            "MongoDbBackupTemp");
    }

    public string? FindExecutable(string? preferredPath)
    {
        foreach (var candidate in GetExecutableCandidates(preferredPath))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Continue to the next discovery location.
            }
        }

        return null;
    }

    public async Task<MongoDumpToolInfo> ValidateToolAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException("The selected mongodump executable does not exist.");
        }

        var fullPath = Path.GetFullPath(executablePath);
        var startInfo = CreateBaseStartInfo(fullPath);
        startInfo.ArgumentList.Add("--version");
        using var job = new WindowsKillOnCloseJob();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("mongodump could not be started.");
            }

            job.Assign(process);
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = $"{await standardOutput}{Environment.NewLine}{await standardError}".Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("The selected executable did not report a mongodump version.");
            }

            var match = VersionRegex().Match(output);
            if (!match.Success
                || !Version.TryParse(match.Value, out var version))
            {
                throw new InvalidOperationException("The mongodump version could not be determined.");
            }

            if (version < MinimumVersion)
            {
                throw new InvalidOperationException(
                    $"mongodump {version} is too old. Install MongoDB Database Tools 100.3.0 or newer.");
            }

            return new MongoDumpToolInfo(fullPath, version, version.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            TryKill(process);
            throw new InvalidOperationException("The selected mongodump executable could not be validated.", exception);
        }
    }

    public Task CleanupStaleFilesAsync(CancellationToken cancellationToken = default) =>
        MongoDumpArchiveWorkspace.CleanupStaleFilesAsync(_temporaryRoot, cancellationToken);

    public async Task<MongoDbBackupResult> RunAsync(
        MongoDumpRunRequest request,
        string connectionString,
        Action processStarted,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(processStarted);

        var startedUtc = DateTimeOffset.UtcNow;
        using var workspace = MongoDumpArchiveWorkspace.Create(
            _temporaryRoot,
            request.ArchivePath,
            request.OverwriteExisting,
            connectionString);
        var startInfo = BuildBackupStartInfo(
            request.ExecutablePath,
            workspace.ConfigPath,
            workspace.PartialArchivePath);
        using var job = new WindowsKillOnCloseJob();
        using var process = new Process { StartInfo = startInfo };
        var outputGate = new object();
        var latestOutput = string.Empty;

        void CaptureOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            var sanitized = MongoDbOutputRedactor.Redact(
                eventArgs.Data,
                connectionString,
                workspace.ConfigPath);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            lock (outputGate)
            {
                latestOutput = sanitized;
            }

            progress?.Report(sanitized);
        }

        process.OutputDataReceived += CaptureOutput;
        process.ErrorDataReceived += CaptureOutput;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("mongodump could not be started.");
            }

            try
            {
                job.Assign(process);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            processStarted();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                process.WaitForExit();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                job.Dispose();
                await process.WaitForExitAsync(CancellationToken.None);
                process.WaitForExit();
                return new MongoDbBackupResult
                {
                    Phase = MongoDbBackupPhase.Cancelled,
                    StartedUtc = startedUtc,
                    FinishedUtc = DateTimeOffset.UtcNow,
                    Message = "Backup cancelled. Incomplete files were removed."
                };
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new MongoDbBackupResult
                {
                    Phase = MongoDbBackupPhase.Cancelled,
                    StartedUtc = startedUtc,
                    FinishedUtc = DateTimeOffset.UtcNow,
                    Message = "Backup cancelled. Incomplete files were removed."
                };
            }

            if (process.ExitCode != 0)
            {
                string detail;
                lock (outputGate)
                {
                    detail = latestOutput;
                }

                var message = string.IsNullOrWhiteSpace(detail)
                    ? $"mongodump failed with exit code {process.ExitCode}."
                    : $"mongodump failed with exit code {process.ExitCode}. {detail}";
                return new MongoDbBackupResult
                {
                    Phase = MongoDbBackupPhase.Faulted,
                    StartedUtc = startedUtc,
                    FinishedUtc = DateTimeOffset.UtcNow,
                    Message = message
                };
            }

            var size = workspace.Complete();
            return new MongoDbBackupResult
            {
                Phase = MongoDbBackupPhase.Succeeded,
                StartedUtc = startedUtc,
                FinishedUtc = DateTimeOffset.UtcNow,
                ArchivePath = request.ArchivePath,
                ArchiveSizeBytes = size,
                Message = "Backup completed successfully."
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                MongoDbOutputRedactor.Redact(
                    exception.Message,
                    connectionString,
                    workspace.ConfigPath),
                exception);
        }
        finally
        {
            process.OutputDataReceived -= CaptureOutput;
            process.ErrorDataReceived -= CaptureOutput;
            connectionString = string.Empty;
        }
    }

    internal static ProcessStartInfo BuildBackupStartInfo(
        string executablePath,
        string configPath,
        string partialArchivePath)
    {
        var startInfo = CreateBaseStartInfo(executablePath);
        startInfo.ArgumentList.Add($"--config={configPath}");
        startInfo.ArgumentList.Add($"--archive={partialArchivePath}");
        startInfo.ArgumentList.Add("--gzip");
        startInfo.WorkingDirectory = Path.GetDirectoryName(partialArchivePath) ?? string.Empty;
        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(string executablePath) => new()
    {
        FileName = executablePath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = false
    };

    private static IEnumerable<string> GetExecutableCandidates(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            yield return preferredPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                var candidateDirectory = directory.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(candidateDirectory))
                {
                    yield return Path.Combine(candidateDirectory, "mongodump.exe");
                }
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var candidate in FindConventionalCandidates(programFiles))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> FindConventionalCandidates(string programFiles)
    {
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            yield break;
        }

        var mongoRoot = Path.Combine(programFiles, "MongoDB");
        foreach (var productFolder in new[] { "Tools", "Server" })
        {
            var root = Path.Combine(mongoRoot, productFolder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> versions;
            try
            {
                versions = Directory.EnumerateDirectories(root).OrderByDescending(path => path).ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var version in versions)
            {
                yield return Path.Combine(version, "bin", "mongodump.exe");
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process has already exited or the job will terminate it when disposed.
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionRegex();
}
