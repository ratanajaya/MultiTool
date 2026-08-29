namespace MultiTool.Modules.BatchFileOrganizer;

public sealed record BatchRenamePlan
{
    public IReadOnlyList<BatchRenameRequest> Requests { get; init; } = [];

    public IReadOnlyList<string> ProposedFileNames { get; init; } = [];

    public string? Error { get; init; }

    public bool IsValid => string.IsNullOrWhiteSpace(Error);

    public bool HasChanges => Requests.Any(request =>
        !string.Equals(request.SourcePath, request.DestinationPath, StringComparison.Ordinal));
}
