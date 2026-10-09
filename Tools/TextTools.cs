using System.Text.Json.Nodes;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

internal static class TextTools
{
    private const int MaxChars = 20000;

    public static IEnumerable<ITool> All(ToolContext ctx)
    {
        yield return new DelegateTool("transform_clipboard",
            "Rewrite the text on the clipboard (summarize, translate, fix grammar, make polite/formal/casual/shorter, or explain) " +
            "and put the result back on the clipboard. The clipboard text is sent to the configured AI model.",
            new JsonObject
            {
                ["action"] = Choice("How to transform the text.",
                    "summarize", "translate", "fix_grammar", "polite", "formal", "casual", "shorter", "explain"),
                ["language"] = Str("Target language for 'translate' (default English).")
            }, ["action"], false,
            async args =>
            {
                var llm = ctx.Llm ?? throw new InvalidOperationException("AI model is not ready.");
                if (!System.Windows.Clipboard.ContainsText()) return "The clipboard has no text.";
                if (!await ctx.AllowCloudAsync("your clipboard text")) return "The user chose not to send the clipboard text.";

                var text = System.Windows.Clipboard.GetText();
                if (text.Length > MaxChars) text = text[..MaxChars];

                var action = args["action"]!.GetValue<string>();
                var language = args["language"]?.GetValue<string>() ?? "English";
                var instruction = action switch
                {
                    "summarize" => "Summarize the following text concisely.",
                    "translate" => $"Translate the following text into {language}.",
                    "fix_grammar" => "Fix the grammar, spelling and punctuation of the following text without changing its meaning or tone.",
                    "polite" => "Rewrite the following text to be more polite and friendly, keeping the meaning.",
                    "formal" => "Rewrite the following text in a formal, professional tone, keeping the meaning.",
                    "casual" => "Rewrite the following text in a relaxed, casual tone, keeping the meaning.",
                    "shorter" => "Rewrite the following text to be much shorter while keeping the key points.",
                    _ => "Explain the following text in simple terms."
                };

                var result = await llm.GenerateAsync(
                    instruction + " Return only the resulting text, with no preamble or quotes.", text);
                if (result.Length == 0) return "The model returned nothing.";

                System.Windows.Clipboard.SetText(result);
                var preview = result.Length > 1500 ? result[..1500] + "..." : result;
                return $"Clipboard updated with:\n{preview}";
            });
    }
}
