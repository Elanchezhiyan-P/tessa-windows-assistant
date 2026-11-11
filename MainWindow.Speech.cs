using System.Windows;
using WinCompanion.Services;

namespace WinCompanion;

// Who understands her recording of your voice: Gemini, an OpenAI-compatible service, or Windows' own recogniser.
public partial class MainWindow
{
    private bool OpenAiSpeechUsable() =>
        !string.IsNullOrWhiteSpace(_settings.LoadApiKey("openai"))
        || Uri.TryCreate(_appSettings.OpenAiBaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private SpeechEngineKind CurrentSpeech() =>
        SpeechRouting.Resolve(_appSettings, !string.IsNullOrWhiteSpace(_settings.LoadApiKey()), OpenAiSpeechUsable());

    private async Task<string?> TranscribeVoiceAsync(byte[] wav)
    {
        switch (CurrentSpeech())
        {
            case SpeechEngineKind.Gemini:
                return await GeminiSpeech.TranscribeAsync(_settings.LoadApiKey()!, wav, _appSettings.SpeechLanguage);
            case SpeechEngineKind.OpenAi:
                return await OpenAiSpeech.TranscribeAsync(_appSettings.OpenAiBaseUrl, _settings.LoadApiKey("openai") ?? "",
                    SpeechRouting.ModelFor(_appSettings), wav, _appSettings.SpeechLanguage);
            default:
                return null;
        }
    }

    private string SelectedSpeechEngine() =>
        new[] { SpeechAuto, SpeechGemini, SpeechOpenAi, SpeechWindows }.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "auto";

    private void SpeechEngine_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateSpeechNote();
    }

    // Says plainly who would get the recording with the choices as they are on screen now.
    private void UpdateSpeechNote()
    {
        if (SpeechEngineNote is null) return;
        var preview = new AppSettings
        {
            Provider = _appSettings.Provider,
            OpenAiBaseUrl = _appSettings.OpenAiBaseUrl,
            SpeechViaGemini = GeminiSpeechSwitch.IsChecked == true,
            SpeechEngine = SelectedSpeechEngine(),
            SpeechModel = SpeechModelBox.Text
        };
        var kind = SpeechRouting.Resolve(preview, !string.IsNullOrWhiteSpace(_settings.LoadApiKey()), OpenAiSpeechUsable());
        var why = kind == SpeechEngineKind.Windows && preview.SpeechViaGemini && preview.SpeechEngine != "windows"
            ? " (Automatic uses Gemini or an OpenAI-compatible service when that is your chosen model and its key is saved; Claude and local models can't listen. Pick a service on purpose to use it anyway; its key is kept from Settings, AI model.)"
            : "";
        SpeechEngineNote.Text = SpeechRouting.Describe(kind, preview) + why;
    }
}
