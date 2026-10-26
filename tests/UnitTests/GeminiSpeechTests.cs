using System.Net;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class GeminiSpeechTests
{
    [Fact]
    public void InstructionHasNoAccentAssumption() =>
        Assert.DoesNotContain("accent", GeminiSpeech.BuildInstruction(""), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void InstructionMentionsTheChosenLanguage() =>
        Assert.Contains("en-IN", GeminiSpeech.BuildInstruction("en-IN"));

    [Fact]
    public void DailyQuotaGetsAFriendlyMessage() =>
        Assert.Contains("today's free Gemini quota",
            GeminiApi.Describe(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"PerDay limit\"}}", null));

    [Fact]
    public void RetryDelayIsParsed() =>
        Assert.Equal(TimeSpan.FromSeconds(12),
            GeminiApi.RetryDelay("{\"error\":{\"details\":[{\"retryDelay\":\"12s\"}]}}"));
}
