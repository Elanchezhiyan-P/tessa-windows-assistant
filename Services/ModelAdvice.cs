namespace WinCompanion.Services;

/// <summary>Which local model this PC can run comfortably, from its memory and graphics card.</summary>
public static class ModelAdvice
{
    private const long Gb = 1024L * 1024 * 1024;

    // A graphics card needs this much dedicated memory before it is worth running the model on it.
    private const long UsableVram = 4 * Gb;

    /// <param name="Model">An Ollama model name that supports tool calling.</param>
    /// <param name="OnGpu">True when the model fits in the graphics card; false means it runs on the processor and will be slower.</param>
    public sealed record Advice(string Model, bool OnGpu, string Text);

    public static Advice Recommend(long ramBytes, long vramBytes)
    {
        var onGpu = vramBytes >= UsableVram;
        // On the processor the model shares memory with Windows and everything else that is open, so count only half.
        var budget = onGpu ? vramBytes : ramBytes / 2;

        // On the processor anything above 7B is too slow to be useful, however much memory there is.
        if (!onGpu) budget = Math.Min(budget, 10 * Gb);

        var model = budget switch
        {
            < 3 * Gb => "qwen2.5:1.5b",
            < 6 * Gb => "qwen2.5:3b",
            < 11 * Gb => "qwen2.5:7b",
            < 24 * Gb => "qwen2.5:14b",
            _ => "qwen2.5:32b"
        };

        var where = onGpu
            ? $"it fits in your graphics card ({SystemInfo.FormatBytes(vramBytes)})"
            : $"it would run on your processor with {SystemInfo.FormatBytes(ramBytes)} of memory, so replies will be slower";
        var text = $"For a local model I'd suggest {model}: {where}.";
        if (model == "qwen2.5:1.5b")
            text += " That is very limited, so Gemini will give you much better answers on this PC.";
        return new Advice(model, onGpu, text);
    }

    public static Advice Recommend(SystemSpecs specs) =>
        Recommend(specs.RamTotalBytes, SystemInfo.MainGpu(specs)?.VramBytes ?? 0);
}
