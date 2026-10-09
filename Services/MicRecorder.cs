using NAudio.Wave;

namespace WinCompanion.Services;

/// <summary>
/// Records one spoken phrase from the default microphone: waits for you to start, stops after a short pause,
/// and returns it as a 16 kHz mono WAV. Quiet microphones (common in laptops) are boosted before it is returned.
/// </summary>
internal static class MicRecorder
{
    public sealed record Recording(byte[]? Wav, double Peak, double Seconds, string Outcome);

    public static async Task<Recording> RecordUtteranceAsync(TimeSpan firstWordTimeout, TimeSpan maxLength, Func<bool> cancelled)
    {
        var format = new WaveFormat(16000, 16, 1);
        var pcm = new MemoryStream();
        var finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        double noise = 0, peak = 0;
        var buffers = 0;
        var speech = false;
        var start = DateTime.UtcNow;
        var lastVoice = start;

        using var wave = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100 };
        wave.DataAvailable += (_, e) =>
        {
            pcm.Write(e.Buffer, 0, e.BytesRecorded);
            var count = e.BytesRecorded / 2;
            double sum = 0;
            for (var i = 0; i < count; i++)
            {
                var v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768.0;
                sum += v * v;
                if (Math.Abs(v) > peak) peak = Math.Abs(v);
            }
            var rms = Math.Sqrt(sum / Math.Max(1, count));
            buffers++;

            // The first moments are assumed to be background noise; speech must stand clearly above it.
            if (buffers <= 3) { noise = Math.Max(noise, rms); return; }
            var now = DateTime.UtcNow;
            if (rms > Math.Max(0.004, noise * 2.5)) { speech = true; lastVoice = now; }

            if (speech && now - lastVoice > TimeSpan.FromSeconds(1.2)) finished.TrySetResult("done");
            else if (!speech && now - start > firstWordTimeout) finished.TrySetResult("silence");
            else if (now - start > maxLength) finished.TrySetResult("done");
            else if (cancelled()) finished.TrySetResult("cancelled");
        };
        wave.RecordingStopped += (_, e) => finished.TrySetResult(e.Exception is null ? "stopped" : "error: " + e.Exception.Message);

        try { wave.StartRecording(); }
        catch (Exception ex) { return new Recording(null, 0, 0, "error: " + ex.Message); }

        var outcome = await finished.Task;
        try { wave.StopRecording(); } catch (Exception) { /* already stopped */ }

        var seconds = (DateTime.UtcNow - start).TotalSeconds;
        if (!speech || outcome is "cancelled" or "silence" || outcome.StartsWith("error"))
            return new Recording(null, peak, seconds, outcome);
        return new Recording(ToWav(pcm.ToArray(), format, peak), peak, seconds, outcome);
    }

    private static byte[] ToWav(byte[] pcm, WaveFormat format, double peak)
    {
        // Bring a quiet recording up to a healthy level (never more than 30x, and clipping is prevented).
        var gain = peak > 0.0001 ? Math.Min(30.0, 0.8 / peak) : 1.0;
        if (gain > 1.2)
        {
            for (var i = 0; i + 1 < pcm.Length; i += 2)
            {
                var s = (int)Math.Clamp(BitConverter.ToInt16(pcm, i) * gain, short.MinValue, short.MaxValue);
                pcm[i] = (byte)(s & 0xFF);
                pcm[i + 1] = (byte)((s >> 8) & 0xFF);
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);                          // PCM
        w.Write((short)format.Channels);
        w.Write(format.SampleRate);
        w.Write(format.AverageBytesPerSecond);
        w.Write((short)format.BlockAlign);
        w.Write((short)format.BitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }
}
