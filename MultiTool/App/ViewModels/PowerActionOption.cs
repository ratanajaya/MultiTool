using MultiTool.Modules.PowerTimer;

namespace MultiTool.Presentation.ViewModels;

public sealed record PowerActionOption(
    PowerAction Value,
    string DisplayName,
    bool IsAvailable);
