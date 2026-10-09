using NAudio.Wave;

namespace WinCompanion.Services;

/// <summary>
/// Records one spoken phrase from the default microphone: waits for you to start, stops after a short pause,
/// and returns it as a 16 kHz mono WAV. Quiet microphones (common in laptops) are boosted before it is returned.
/// </summary>
internal static class MicRecorder
{
    /// <param name="Peak">A typical loud moment of the recording (not a single click), 0 to 1.</param>
    public sealed record Recording(byte[]? Wav, double Peak, double Seconds, string Outcome, string Detail = "");

    public static async Task<Recording> RecordUtteranceAsync(TimeSpan firstWordTimeout, TimeSpan maxLength, Func<bool> cancelled,
        Action<double>? onLevel = null, Func<bool>? stopNow = null)
    {
        var format = new WaveFormat(16000, 16, 1);
        var pcm = new MemoryStream();
        var finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peaks = new List<double>();
        var noise = double.MaxValue;
        var buffers = 0;
        var speech = false;
        var start = DateTime.UtcNow;
        var lastVoice = start;
        double speechLevel = 0;

        using var wave = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100 };
        wave.DataAvailable += (_, e) =>
        {
            buffers++;
            // Many microphones make a loud click as they open. Ignore it, and keep it out of the recording.
            if (buffers <= 2) return;

            pcm.Write(e.Buffer, 0, e.BytesRecorded);
            var count = e.BytesRecorded / 2;
            double sum = 0, top = 0;
            for (var i = 0; i < count; i++)
            {
                var v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768.0;
                sum += v * v;
                if (Math.Abs(v) > top) top = Math.Abs(v);
            }
            var rms = Math.Sqrt(sum / Math.Max(1, count));
            peaks.Add(top);
            onLevel?.Invoke(rms);

            // The quietest moment so far is the background noise; speech must stand clearly above it, but never needs to be
            // shouted: the bar is capped low, so a soft voice on a laptop microphone still counts.
            noise = Math.Min(rms, noise * 1.02); // follows the quiet moments, and creeps up so a noisy room can't pin it low forever
            var threshold = Math.Clamp(noise * 3, 0.0035, 0.03);
            var now = DateTime.UtcNow;

            // How loud you have been speaking (quick to rise, slow to fall). The end of a sentence is when you drop well below it.
            if (rms > threshold) { speech = true; speechLevel = Math.Max(rms, speechLevel * 0.97); }
            var quiet = rms <= threshold || (speech && rms < speechLevel * 0.35);
            if (!quiet) lastVoice = now;

            if (stopNow?.Invoke() == true) finished.TrySetResult(speech ? "done" : "silence");
            else if (speech && now - lastVoice > TimeSpan.FromSeconds(0.8)) finished.TrySetResult("done");
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
        var detail = $"noise={noise:F4} voice={speechLevel:F4}";
        var typicalPeak = 0.0;
        if (peaks.Count > 0)
        {
            peaks.Sort();
            typicalPeak = peaks[(int)(0.98 * (peaks.Count - 1))];
        }

        if (!speech || outcome is "cancelled" or "silence" || outcome.StartsWith("error"))
            return new Recording(null, typicalPeak, seconds, outcome, detail);
        return new Recording(ToWav(pcm.ToArray(), format, typicalPeak), typicalPeak, seconds, outcome, detail);
    }

    private static byte[] ToWav(byte[] pcm, WaveFormat format, double typicalPeak)
    {
        // Bring a quiet recording up to a healthy level (never more than 30x; clipping is prevented sample by sample).
        var gain = typicalPeak is > 0.0005 and < 0.6 ? Math.Min(30.0, 0.7 / typicalPeak) : 1.0;
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
