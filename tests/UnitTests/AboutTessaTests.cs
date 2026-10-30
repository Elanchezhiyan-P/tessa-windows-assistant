using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class AboutTessaTests
{
    [Theory]
    [InlineData("who created you?")]
    [InlineData("Who made you")]
    [InlineData("who built you and why")]
    [InlineData("who are you")]
    [InlineData("tell me about yourself")]
    [InlineData("tell me about you")]
    [InlineData("introduce yourself")]
    [InlineData("which model are you using?")]
    [InlineData("are you gemini?")]
    [InlineData("are you chatgpt")]
    [InlineData("who is your creator")]
    public void RecognisesQuestionsAboutHer(string text) => Assert.True(AboutTessa.IsAboutQuestion(text));

    [Theory]
    [InlineData("what can you do?")]
    [InlineData("What features do you have")]
    [InlineData("what are your features")]
    [InlineData("list your abilities")]
    [InlineData("how can you help me")]
    [InlineData("what can I ask")]
    [InlineData("help")]
    public void RecognisesQuestionsAboutFeatures(string text) => Assert.True(AboutTessa.IsFeaturesQuestion(text));

    [Theory]
    [InlineData("who created the file report.docx")]
    [InlineData("what can you tell me about the weather in Paris and how it affects the flights I booked for next week please")]
    [InlineData("remind me to help mom at 5pm")]
    [InlineData("open notepad")]
    public void LeavesOtherRequestsAlone(string text)
    {
        Assert.False(AboutTessa.IsAboutQuestion(text));
        Assert.False(AboutTessa.IsFeaturesQuestion(text));
    }

    [Fact]
    public void AnswersNameTheCreatorNotTheModel()
    {
        var about = AboutTessa.About("Tessa", "Google Gemini");
        Assert.Contains("Elanchezhiyan P", about);
        Assert.Contains("Tessa", about);
        Assert.Contains("Hey Tessa", AboutTessa.Features("Tessa"));
    }
}
