using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class SpeechRoutingTests
{
    private static AppSettings S(string provider, string engine = "auto", bool cloudSpeech = true) =>
        new() { Provider = provider, SpeechEngine = engine, SpeechViaGemini = cloudSpeech };

    [Theory]
    [InlineData("gemini", true, false, SpeechEngineKind.Gemini)]
    [InlineData("gemini", false, true, SpeechEngineKind.Windows)]   // no Gemini key
    [InlineData("openai", false, true, SpeechEngineKind.OpenAi)]
    [InlineData("openai", true, false, SpeechEngineKind.Windows)]   // no key for the service
    [InlineData("claude", true, true, SpeechEngineKind.Windows)]    // Claude cannot listen, so nothing leaves on its own
    [InlineData("local", true, true, SpeechEngineKind.Windows)]
    public void AutomaticFollowsTheChosenModelOnlyWhenItCanListen(string provider, bool gemini, bool openAi, SpeechEngineKind expected) =>
        Assert.Equal(expected, SpeechRouting.Resolve(S(provider), gemini, openAi));

    [Theory]
    [InlineData("claude", "openai", false, true, SpeechEngineKind.OpenAi)]   // a deliberate choice works with any model
    [InlineData("claude", "gemini", true, false, SpeechEngineKind.Gemini)]
    [InlineData("openai", "gemini", true, true, SpeechEngineKind.Gemini)]
    [InlineData("gemini", "windows", true, true, SpeechEngineKind.Windows)]
    [InlineData("claude", "openai", false, false, SpeechEngineKind.Windows)] // chosen, but there is no key
    public void APickedServiceIsUsedWhateverTheModelIs(string provider, string engine, bool gemini, bool openAi, SpeechEngineKind expected) =>
        Assert.Equal(expected, SpeechRouting.Resolve(S(provider, engine), gemini, openAi));

    [Fact]
    public void TheMasterSwitchOffMeansNothingIsSentAnywhere() =>
        Assert.Equal(SpeechEngineKind.Windows, SpeechRouting.Resolve(S("gemini", "gemini", cloudSpeech: false), true, true));

    [Theory]
    [InlineData("nonsense", "auto")]
    [InlineData(null, "auto")]
    [InlineData("OPENAI", "openai")]
    public void UnknownChoicesFallBackToAutomatic(string? choice, string expected) => Assert.Equal(expected, SpeechRouting.Normalize(choice));

    [Theory]
    [InlineData("https://api.openai.com/v1", "", "whisper-1")]
    [InlineData("https://api.groq.com/openai/v1", "", "whisper-large-v3-turbo")]
    [InlineData("https://openrouter.ai/api/v1", "", "whisper-1")]
    [InlineData("https://api.openai.com/v1", " gpt-4o-transcribe ", "gpt-4o-transcribe")]
    public void TheSpeechModelDefaultsByService(string url, string custom, string expected) =>
        Assert.Equal(expected, SpeechRouting.ModelFor(new AppSettings { OpenAiBaseUrl = url, SpeechModel = custom }));

    [Theory]
    [InlineData("en-IN", "en")]
    [InlineData("ta-IN", "ta")]
    [InlineData("fil", "fil")]
    [InlineData("", "")]
    [InlineData("not a tag", "")]
    [InlineData("e1", "")]
    public void TheLanguageTagBecomesTheTwoLetterLanguage(string tag, string expected) => Assert.Equal(expected, OpenAiSpeech.LanguageOf(tag));

    [Fact]
    public void TheDescriptionNamesWhoGetsTheAudio()
    {
        var settings = new AppSettings { Provider = "openai", OpenAiBaseUrl = "https://api.groq.com/openai/v1" };
        Assert.Contains("api.groq.com", SpeechRouting.Describe(SpeechEngineKind.OpenAi, settings));
        Assert.Contains("Gemini", SpeechRouting.Describe(SpeechEngineKind.Gemini, settings));
        Assert.Contains("on this PC", SpeechRouting.Describe(SpeechEngineKind.Windows, settings));
    }

    [Fact]
    public async Task TheRecordingIsUploadedWithTheKeyTheModelAndTheLanguage()
    {
        using var server = new FakeChatServer { Reply = """{"text":"  open the calendar  "}""" };
        var text = await OpenAiSpeech.TranscribeAsync(server.BaseUrl, "sk-speech", "whisper-1", new byte[] { 1, 2, 3, 4 }, "en-IN");

        Assert.Equal("open the calendar", text);
        var (auth, _) = server.Requests.Single();
        Assert.Equal("Bearer sk-speech", auth);
        var (path, type, raw) = server.Raw.Single();
        Assert.EndsWith("/audio/transcriptions", path);
        Assert.StartsWith("multipart/form-data", type);
        Assert.Contains("whisper-1", raw);
        Assert.Contains("name=language", raw.Replace("\"", ""));
        Assert.Contains("speech.wav", raw);
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(404, "no speech model")]
    [InlineData(429, "limiting requests")]
    [InlineData(503, "busy")]
    public async Task FailuresAreExplained(int status, string expected)
    {
        using var server = new FakeChatServer { Status = status, Reply = "{}" };
        var error = await Assert.ThrowsAsync<LlmException>(() => OpenAiSpeech.TranscribeAsync(server.BaseUrl, "k", "m", new byte[] { 1 }));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task AnUnreachableServiceSaysSo()
    {
        var error = await Assert.ThrowsAsync<LlmException>(() => OpenAiSpeech.TranscribeAsync("http://127.0.0.1:1/v1", "k", "m", new byte[] { 1 }));
        Assert.Contains("couldn't reach", error.Message);
    }
}
