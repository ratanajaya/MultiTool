namespace MultiTool.Modules.AudioOutputKeepAlive;

public sealed record AudioOutputKeepAliveSettings
{
    public bool Enabled { get; init; }

    public string? DeviceId { get; init; }

    public int IntervalSeconds { get; init; } = 60;

    public int BurstDurationMilliseconds { get; init; } = 250;

    public float Amplitude { get; init; } = 0.00008f;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (IntervalSeconds is < 1 or > 86_400)
        {
            errors.Add("Interval must be between 1 second and 24 hours.");
        }

        if (BurstDurationMilliseconds is < 10 or > 5_000)
        {
            errors.Add("Burst duration must be between 10 and 5,000 milliseconds.");
        }

        if (!float.IsFinite(Amplitude) || Amplitude is < 0.000001f or > 0.01f)
        {
            errors.Add("Amplitude must be between 0.000001 and 0.01.");
        }

        return errors;
    }
}
