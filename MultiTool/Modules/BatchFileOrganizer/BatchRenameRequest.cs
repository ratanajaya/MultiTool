namespace MultiTool.Modules.BatchFileOrganizer;

public sealed record BatchRenameRequest(
    string SourcePath,
    string DestinationPath);
