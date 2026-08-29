using System.IO;

namespace MultiTool.Modules.BatchFileOrganizer;

public sealed class BatchFileRenameService(IBatchFileSystem fileSystem)
{
    private readonly IBatchFileSystem _fileSystem = fileSystem;

    public string? ValidateCurrentConflicts(BatchRenamePlan plan)
    {
        if (!plan.IsValid)
        {
            return plan.Error;
        }

        var sourcePaths = plan.Requests
            .Select(request => request.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var request in plan.Requests)
        {
            if (_fileSystem.DirectoryExists(request.DestinationPath))
            {
                return $"A directory already uses the generated name '{Path.GetFileName(request.DestinationPath)}'.";
            }

            if (_fileSystem.FileExists(request.DestinationPath)
                && !sourcePaths.Contains(request.DestinationPath))
            {
                return $"A file already uses the generated name '{Path.GetFileName(request.DestinationPath)}'.";
            }
        }

        return null;
    }

    public Task<BatchRenameResult> RenameAsync(
        string folderPath,
        BatchRenamePlan plan,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RenameCore(folderPath, plan, cancellationToken), CancellationToken.None);

    private BatchRenameResult RenameCore(
        string folderPath,
        BatchRenamePlan plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid)
        {
            throw new InvalidOperationException(plan.Error);
        }

        var fullFolderPath = Path.GetFullPath(folderPath);
        var sourcePathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinationPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in plan.Requests)
        {
            var sourcePath = Path.GetFullPath(request.SourcePath);
            var destinationPath = Path.GetFullPath(request.DestinationPath);
            if (!string.Equals(Path.GetDirectoryName(sourcePath), fullFolderPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetDirectoryName(destinationPath),
                    fullFolderPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Every source and generated filename must remain directly inside the selected folder.");
            }

            if (!sourcePathSet.Add(sourcePath))
            {
                throw new InvalidOperationException("The rename plan contains the same source file more than once.");
            }

            if (!destinationPathSet.Add(destinationPath))
            {
                throw new InvalidOperationException("The rename plan generates the same destination more than once.");
            }
        }

        var expectedSources = plan.Requests
            .Select(request => Path.GetFullPath(request.SourcePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentSources = _fileSystem.EnumerateTopLevelFiles(fullFolderPath)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedSources.SetEquals(currentSources))
        {
            throw new InvalidOperationException(
                "The folder contents changed after they were loaded. Reload the folder before renaming.");
        }

        var conflict = ValidateCurrentConflicts(plan);
        if (conflict is not null)
        {
            throw new IOException(conflict);
        }

        var moves = plan.Requests
            .Where(request => !string.Equals(
                request.SourcePath,
                request.DestinationPath,
                StringComparison.Ordinal))
            .Select(request => new PendingMove(
                request.SourcePath,
                CreateTemporaryPath(fullFolderPath),
                request.DestinationPath))
            .ToList();
        if (moves.Count == 0)
        {
            return new BatchRenameResult
            {
                Succeeded = true,
                RolledBack = true,
                Message = "The files already have the proposed names."
            };
        }

        try
        {
            foreach (var move in moves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _fileSystem.MoveFile(move.SourcePath, move.TemporaryPath);
                move.IsStaged = true;
            }

            foreach (var move in moves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _fileSystem.MoveFile(move.TemporaryPath, move.DestinationPath);
                move.IsFinalized = true;
            }

            return new BatchRenameResult
            {
                Succeeded = true,
                RolledBack = true,
                Message = $"Renamed {moves.Count} file(s)."
            };
        }
        catch (Exception exception)
        {
            var unrestored = RollBack(moves);
            var cancelled = exception is OperationCanceledException;
            return new BatchRenameResult
            {
                Succeeded = false,
                RolledBack = unrestored.Count == 0,
                UnrestoredPaths = unrestored,
                Message = unrestored.Count == 0
                    ? cancelled
                        ? "The rename was cancelled and all files were restored."
                        : $"The rename failed and all files were restored: {exception.Message}"
                    : $"The rename failed and some files could not be restored: {exception.Message}"
            };
        }
    }

    private List<string> RollBack(IReadOnlyList<PendingMove> moves)
    {
        var unrestored = new List<string>();

        foreach (var move in moves.Reverse().Where(move => move.IsFinalized))
        {
            try
            {
                if (_fileSystem.FileExists(move.DestinationPath)
                    && !_fileSystem.FileExists(move.TemporaryPath))
                {
                    _fileSystem.MoveFile(move.DestinationPath, move.TemporaryPath);
                }
            }
            catch
            {
                unrestored.Add(move.DestinationPath);
            }
        }

        foreach (var move in moves.Reverse().Where(move => move.IsStaged))
        {
            try
            {
                if (_fileSystem.FileExists(move.TemporaryPath)
                    && !_fileSystem.FileExists(move.SourcePath))
                {
                    _fileSystem.MoveFile(move.TemporaryPath, move.SourcePath);
                }
            }
            catch
            {
                unrestored.Add(move.TemporaryPath);
            }
        }

        return unrestored.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string CreateTemporaryPath(string folderPath)
    {
        string candidate;
        do
        {
            candidate = Path.Combine(folderPath, $".multitool-rename-{Guid.NewGuid():N}.tmp");
        }
        while (_fileSystem.FileExists(candidate) || _fileSystem.DirectoryExists(candidate));

        return candidate;
    }

    private sealed class PendingMove(
        string sourcePath,
        string temporaryPath,
        string destinationPath)
    {
        public string SourcePath { get; } = sourcePath;

        public string TemporaryPath { get; } = temporaryPath;

        public string DestinationPath { get; } = destinationPath;

        public bool IsStaged { get; set; }

        public bool IsFinalized { get; set; }
    }
}
