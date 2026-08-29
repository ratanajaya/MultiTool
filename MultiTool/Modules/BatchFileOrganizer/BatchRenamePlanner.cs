using System.Globalization;
using System.IO;

namespace MultiTool.Modules.BatchFileOrganizer;

public static class BatchRenamePlanner
{
    private const int MaximumWindowsPathLength = 259;
    private const int MaximumFileNameLength = 255;

    public static BatchRenamePlan Create(
        string folderPath,
        IReadOnlyList<string> sourcePaths,
        string prefix,
        string startNumberText,
        string paddingText)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Invalid("Select a folder first.");
        }

        if (!int.TryParse(startNumberText, NumberStyles.None, CultureInfo.InvariantCulture, out var startNumber)
            || startNumber < 0)
        {
            return Invalid("Starting number must be a non-negative whole number.");
        }

        if (!int.TryParse(paddingText, NumberStyles.None, CultureInfo.InvariantCulture, out var padding)
            || padding is < 1 or > 9)
        {
            return Invalid("Padding must be a whole number from 1 through 9.");
        }

        if (sourcePaths.Count > 0 && startNumber > int.MaxValue - (sourcePaths.Count - 1))
        {
            return Invalid("The starting number and file count exceed the supported number range.");
        }

        string fullFolderPath;
        try
        {
            fullFolderPath = Path.GetFullPath(folderPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid($"The folder path is invalid: {exception.Message}");
        }

        var requests = new List<BatchRenameRequest>(sourcePaths.Count);
        var proposedNames = new List<string>(sourcePaths.Count);
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sourcePaths.Count; index++)
        {
            var number = startNumber + index;
            var extension = Path.GetExtension(sourcePaths[index]);
            var proposedName = $"{prefix}{number.ToString($"D{padding}", CultureInfo.InvariantCulture)}{extension}";
            proposedNames.Add(proposedName);

            var fileNameError = ValidateFileName(proposedName);
            if (fileNameError is not null)
            {
                return Invalid(fileNameError, proposedNames);
            }

            var destinationPath = Path.Combine(fullFolderPath, proposedName);
            if (destinationPath.Length > MaximumWindowsPathLength)
            {
                return Invalid(
                    $"The generated path for '{proposedName}' is longer than {MaximumWindowsPathLength} characters.",
                    proposedNames);
            }

            if (!targetPaths.Add(destinationPath))
            {
                return Invalid($"More than one file would be named '{proposedName}'.", proposedNames);
            }

            string sourcePath;
            try
            {
                sourcePath = Path.GetFullPath(sourcePaths[index]);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Invalid($"A source path is invalid: {exception.Message}", proposedNames);
            }

            if (!string.Equals(
                    Path.GetDirectoryName(sourcePath),
                    fullFolderPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Invalid("Every source file must be directly inside the selected folder.", proposedNames);
            }

            requests.Add(new BatchRenameRequest(sourcePath, destinationPath));
        }

        return new BatchRenamePlan
        {
            Requests = requests,
            ProposedFileNames = proposedNames
        };
    }

    private static string? ValidateFileName(string fileName)
    {
        if (fileName.Length > MaximumFileNameLength)
        {
            return $"The generated filename '{fileName}' is longer than {MaximumFileNameLength} characters.";
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"The generated filename '{fileName}' contains a character Windows does not allow.";
        }

        if (fileName.EndsWith(' ') || fileName.EndsWith('.'))
        {
            return $"The generated filename '{fileName}' cannot end with a space or period.";
        }

        return null;
    }

    private static BatchRenamePlan Invalid(
        string error,
        IReadOnlyList<string>? proposedNames = null) =>
        new()
        {
            Error = error,
            ProposedFileNames = proposedNames ?? []
        };
}
