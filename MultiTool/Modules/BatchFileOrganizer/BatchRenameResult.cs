namespace MultiTool.Modules.BatchFileOrganizer;

public sealed record BatchRenameResult
{
    public bool Succeeded { get; init; }

    public bool RolledBack { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> UnrestoredPaths { get; init; } = [];
}
