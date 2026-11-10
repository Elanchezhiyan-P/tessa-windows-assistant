namespace WinCompanion.Services;

/// <summary>Reads "/model claude claude-sonnet-5-5" and applies it to the settings.</summary>
public static class ModelSwitch
{
    public const string Usage = "Use /model gemini, /model claude [model], /model openai [model] or /model local.";

    /// <summary>The provider named first (null when it is not one) and the model name after it, if any.</summary>
    public static (string? Provider, string? Model) Parse(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, null);
        var provider = AppSettings.Providers.FirstOrDefault(p => p.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        // "chatgpt" and "anthropic" are what people call them.
        provider ??= parts[0].ToLowerInvariant() switch { "chatgpt" or "gpt" => "openai", "anthropic" => "claude", _ => null };
        return (provider, parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>Puts the choice into the settings. Returns the sentence to show, and whether anything changed.</summary>
    public static (string Reply, bool Changed) Apply(AppSettings settings, string provider, string? model)
    {
        var modelChanged = false;
        if (model is { Length: > 0 })
        {
            if (model.Length > 100 || model.Any(char.IsControl) || model.Contains(' ')) return ("That doesn't look like a model name.", false);
            switch (provider)
            {
                case "claude": modelChanged = settings.ClaudeModel != model; settings.ClaudeModel = model; break;
                case "openai": modelChanged = settings.OpenAiModel != model; settings.OpenAiModel = model; break;
                case "local": modelChanged = settings.LocalModel != model; settings.LocalModel = model; break;
                default: return ("Gemini's model is fixed; there is no model to choose.", false);
            }
        }

        var providerChanged = settings.Provider != provider;
        settings.Provider = provider;
        if (!providerChanged && !modelChanged) return ("Already using that model.", false);
        return ($"Switched to {settings.BrainDescription}.", true);
    }

    public static string Describe(AppSettings settings) =>
        $"Using {settings.BrainDescription}. {Usage}";
}
