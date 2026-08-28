using NAudio.Wave;

const int SecondsBetweenBeeps = 60;
const int BeepDurationMs = 250;

// Very low amplitude. Increase slightly if your headset still powers off.
// Keep tiny to remain inaudible.
const float Amplitude = 0.00008f;

// Audio format
const int SampleRate = 48000;
const int Channels = 2;

Console.WriteLine($"SilentSound: playing a near-silent burst every {SecondsBetweenBeeps} seconds.");
Console.WriteLine("Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true;
	cts.Cancel();
};

using var output = new WaveOutEvent { DesiredLatency = 150 };

while (!cts.IsCancellationRequested)
{
	try
	{
		Console.WriteLine($"Playing burst at {DateTime.Now:T}");
        PlayBurst(output);
	}
	catch (Exception ex)
	{
		Console.Error.WriteLine($"Audio error: {ex.Message}");
	}

	try
	{
		await Task.Delay(TimeSpan.FromSeconds(SecondsBetweenBeeps), cts.Token);
	}
	catch (OperationCanceledException)
	{
		break;
	}
}

void PlayBurst(WaveOutEvent waveOut)
{
	var provider = new NearSilentNoiseProvider(SampleRate, Channels, Amplitude);
	provider.SetBurstDuration(TimeSpan.FromMilliseconds(BeepDurationMs));

	waveOut.Stop();
	waveOut.Init(provider);
	waveOut.Play();

	// Block until this burst finishes (or playback stops for any reason).
	while (waveOut.PlaybackState == PlaybackState.Playing)
	{
		Thread.Sleep(10);
	}
}

internal sealed class NearSilentNoiseProvider : ISampleProvider
{
	private readonly WaveFormat _format;
	private readonly float _amplitude;
	private int _samplesRemaining;
	private uint _state = 0x12345678;

	public NearSilentNoiseProvider(int sampleRate, int channels, float amplitude)
	{
		_format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
		_amplitude = amplitude;
	}

	public WaveFormat WaveFormat => _format;

	public void SetBurstDuration(TimeSpan duration)
	{
		var totalSamples = (int)(_format.SampleRate * duration.TotalSeconds) * _format.Channels;
		_samplesRemaining = Math.Max(0, totalSamples);
	}

	public int Read(float[] buffer, int offset, int count)
	{
		if (_samplesRemaining <= 0)
			return 0;

		var toWrite = Math.Min(count, _samplesRemaining);

		for (var i = 0; i < toWrite; i++)
		{
			_state = 1664525u * _state + 1013904223u; // LCG
			var r = ((_state >> 9) & 0x007FFFFF) / 8388608f; // [0,1)
			var n = (r * 2f) - 1f; // [-1,1)
			buffer[offset + i] = n * _amplitude;
		}

		_samplesRemaining -= toWrite;
		return toWrite;
	}
}
