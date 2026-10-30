using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class SlashCommandTests
{
    [Theory]
    [InlineData("/clear", "clear", "")]
    [InlineData("  /Export  my topic ", "export", "my topic")]
    public void ParsesNameAndArguments(string input, string name, string args) =>
        Assert.Equal((name, args), SlashCommands.Parse(input));

    [Theory]
    [InlineData("clear")]
    [InlineData("/")]
    [InlineData("/ clear")]
    [InlineData("//comment")]
    [InlineData("/etc/hosts is a path")]
    public void IgnoresOrdinaryText(string input) => Assert.Null(SlashCommands.Parse(input));

    [Fact]
    public void SuggestsByPrefixOnly()
    {
        Assert.Contains(SlashCommands.Suggest("/"), c => c.Name == "clear");
        Assert.Equal(["clear"], SlashCommands.Suggest("/cle").Select(c => c.Name));
        Assert.Equal(["clear", "clipboard"], SlashCommands.Suggest("/cl").Select(c => c.Name));
        Assert.Empty(SlashCommands.Suggest("/clear now"));
        Assert.Empty(SlashCommands.Suggest("hello"));
    }

    [Fact]
    public void EveryFeatureCommandIsRegistered()
    {
        foreach (var name in new[] { "briefing", "focus", "routine", "ocr", "note", "model", "voice" })
            Assert.NotNull(SlashCommands.Find(name));
        Assert.True(SlashCommands.Find("note")!.NeedsArgs);
        Assert.False(SlashCommands.Find("focus")!.NeedsArgs);
    }

    [Fact]
    public void CommandNamesAreUniqueAndTypeable() =>
        Assert.All(SlashCommands.All, c =>
        {
            Assert.Equal(c.Name, c.Name.ToLowerInvariant());
            Assert.Equal(c.Name, SlashCommands.Parse("/" + c.Name)?.Name);
            Assert.StartsWith("/" + c.Name, c.Usage);
        });

    [Fact]
    public void NamesDoNotRepeat() =>
        Assert.Equal(SlashCommands.All.Count, SlashCommands.All.Select(c => c.Name).Distinct().Count());

    [Fact]
    public void HelpListsEveryCommand()
    {
        var help = SlashCommands.HelpText();
        foreach (var command in SlashCommands.All) Assert.Contains(command.Usage, help);
    }
}
