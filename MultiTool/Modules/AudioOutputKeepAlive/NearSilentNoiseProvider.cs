using NAudio.Wave;

namespace MultiTool.Modules.AudioOutputKeepAlive;

internal sealed class NearSilentNoiseProvider : ISampleProvider
{
    private readonly WaveFormat _format;
    private readonly float _amplitude;
    private int _samplesRemaining;
    private uint _state = 0x12345678;

    public NearSilentNoiseProvider(int sampleRate, int channels, float amplitude, TimeSpan duration)
    {
        _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _amplitude = amplitude;
        _samplesRemaining = Math.Max(
            0,
            (int)(_format.SampleRate * duration.TotalSeconds) * _format.Channels);
    }

    public WaveFormat WaveFormat => _format;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_samplesRemaining <= 0)
        {
            return 0;
        }

        var toWrite = Math.Min(count, _samplesRemaining);
        for (var i = 0; i < toWrite; i++)
        {
            _state = 1664525u * _state + 1013904223u;
            var randomValue = ((_state >> 9) & 0x007FFFFF) / 8388608f;
            buffer[offset + i] = ((randomValue * 2f) - 1f) * _amplitude;
        }

        _samplesRemaining -= toWrite;
        return toWrite;
    }
}
