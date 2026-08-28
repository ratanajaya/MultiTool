namespace MultiTool.Modules.PowerTimer;

public sealed record PowerTimerSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public PowerAction Action { get; init; }

    public DateTimeOffset TargetUtc { get; init; }

    public bool WarningSent { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Id == Guid.Empty)
        {
            errors.Add("The timer identifier is invalid.");
        }

        if (!Enum.IsDefined(Action))
        {
            errors.Add("The selected power action is invalid.");
        }

        return errors;
    }
}
