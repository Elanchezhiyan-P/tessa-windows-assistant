using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class BasicCommandsTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 14, 5, 0);

    private static BasicCall? Parse(string text, bool hasModel = false) => BasicCommands.Parse(text, Now, hasModel);

    [Theory]
    [InlineData("open notepad", "open_target", "notepad")]
    [InlineData("Open the calculator", "open_target", "calc")]
    [InlineData("please open settings", "open_target", "ms-settings:")]
    [InlineData("open downloads", "open_target", "shell:Downloads")]
    [InlineData("launch visual studio code", "open_target", "visual studio code")]
    [InlineData("open example.com", "open_target", "https://example.com")]
    [InlineData("go to https://news.ycombinator.com", "open_target", "https://news.ycombinator.com")]
    [InlineData("open C:\\Temp", "open_target", "C:\\Temp")]
    public void OpensThings(string said, string tool, string target)
    {
        var call = Parse(said)!;
        Assert.Equal(tool, call.Tool);
        Assert.Equal(target, call.Args!["target"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("volume up", "up", null)]
    [InlineData("turn the volume down", "down", null)]
    [InlineData("louder", "up", null)]
    [InlineData("volume up 10", "up", 5)]
    [InlineData("quieter", "down", null)]
    public void ChangesTheVolume(string said, string action, int? steps)
    {
        var call = Parse(said)!;
        Assert.Equal("change_volume", call.Tool);
        Assert.Equal(action, call.Args!["action"]!.GetValue<string>());
        Assert.Equal(steps, call.Args["steps"]?.GetValue<int>());
    }

    [Theory]
    [InlineData("mute")]
    [InlineData("unmute")]
    [InlineData("mute the sound")]
    public void Mutes(string said) => Assert.Equal("mute_toggle", Parse(said)!.Args!["action"]!.GetValue<string>());

    [Theory]
    [InlineData("take a screenshot", "screen")]
    [InlineData("screenshot", "screen")]
    [InlineData("screenshot of this window", "active_window")]
    public void TakesScreenshots(string said, string scope)
    {
        var call = Parse(said)!;
        Assert.Equal("save_screenshot", call.Tool);
        Assert.Equal(scope, call.Args!["scope"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("minimize chrome", "minimize", "chrome")]
    [InlineData("maximise the notepad window", "maximize", "notepad")]
    [InlineData("switch to spotify", "focus", "spotify")]
    [InlineData("snap chrome left", "snap_left", "chrome")]
    [InlineData("snap the notepad to the right", "snap_right", "notepad")]
    public void ControlsWindows(string said, string action, string target)
    {
        var call = Parse(said)!;
        Assert.Equal("window_action", call.Tool);
        Assert.Equal(action, call.Args!["action"]!.GetValue<string>());
        Assert.Equal(target, call.Args["target"]!.GetValue<string>());
    }

    [Fact]
    public void ClosingAndLockingAreToolsThatAskFirst()
    {
        Assert.Equal("close_window", Parse("close notepad")!.Tool);
        Assert.Equal("lock_screen", Parse("lock my screen")!.Tool);
    }

    [Theory]
    [InlineData("what time is it", "It is 2:05 PM.")]
    [InlineData("time", "It is 2:05 PM.")]
    [InlineData("what's the date", "Today is Tuesday, 10 November 2026.")]
    [InlineData("what day is it", "Today is Tuesday, 10 November 2026.")]
    public void AnswersTimeAndDateItself(string said, string reply)
    {
        var call = Parse(said)!;
        Assert.Null(call.Tool);
        Assert.Equal(reply, call.Reply);
    }

    [Theory]
    [InlineData("weather", null)]
    [InlineData("what's the weather in Chennai", "Chennai")]
    [InlineData("weather for new delhi today", "new delhi")]
    public void AsksForTheWeather(string said, string? city)
    {
        var call = Parse(said)!;
        Assert.Equal("get_weather", call.Tool);
        Assert.Equal(city, call.Args!["city"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("brightness 60", 60)]
    [InlineData("set brightness to 30%", 30)]
    public void SetsBrightness(string said, int percent) => Assert.Equal(percent, Parse(said)!.Args!["percent"]!.GetValue<int>());

    [Fact]
    public void BrightnessAboveAHundredIsNotACommand() => Assert.Null(Parse("brightness 500"));

    [Theory]
    [InlineData("pause", "play_pause")]
    [InlineData("play", "play_pause")]
    [InlineData("next song", "next")]
    [InlineData("previous track", "previous")]
    [InlineData("stop the music", "stop")]
    public void ControlsMedia(string said, string action) => Assert.Equal(action, Parse(said)!.Args!["action"]!.GetValue<string>());

    [Theory]
    [InlineData("dark mode", "dark")]
    [InlineData("switch to light mode", "light")]
    public void SwitchesWindowsTheme(string said, string mode) => Assert.Equal(mode, Parse(said)!.Args!["mode"]!.GetValue<string>());

    [Theory]
    [InlineData("why is my pc slow")]
    [InlineData("system info")]
    public void ReportsOnThePc(string said) => Assert.Equal("get_system_info", Parse(said)!.Tool);

    [Fact]
    public void SearchesTheWebOnlyWhenThereIsNoModel()
    {
        Assert.Equal("web_search", Parse("search for pizza near me")!.Tool);
        Assert.Equal("web_search", Parse("google best laptops")!.Tool);
        Assert.Null(Parse("search for pizza near me", hasModel: true)); // a model answers it better
    }

    [Fact]
    public void StartIsOnlyACommandWhenThereIsNoModel()
    {
        Assert.Equal("open_target", Parse("start notepad")!.Tool);
        Assert.Null(Parse("start notepad", hasModel: true));
    }

    // Real sentences must reach the model instead of being taken for commands.
    [Theory]
    [InlineData("open a ticket for the printer and email it to Sam")]
    [InlineData("open the file and tell me what it says")]
    [InlineData("how do I open the task manager?")]
    [InlineData("why is the volume so low in this video")]
    [InlineData("close the deal with the client and summarize")]
    [InlineData("open a new chat")]
    [InlineData("what is the capital of France")]
    [InlineData("play something relaxing by Coldplay")]
    [InlineData("minimize the amount of files in my downloads folder please")]
    [InlineData("start a timer for 5 minutes")]
    [InlineData("start a focus session")]
    [InlineData("remind me to open the window at 5")]
    [InlineData("/theme light")]
    [InlineData("")]
    public void QuestionsAndBigRequestsAreLeftAlone(string said) => Assert.Null(Parse(said));

    [Fact]
    public void OverlongTextIsNeverACommand() => Assert.Null(Parse("open " + new string('a', 120)));

    [Fact]
    public void WithoutAModelTheHelpListsWhatStillWorks()
    {
        var help = BasicCommands.OfflineHelp("Tessa");
        Assert.Contains("open notepad", help);
        Assert.Contains("volume up", help);
        Assert.Contains("Settings, AI model", help);
        Assert.Contains("remind me in 10 minutes", help);
    }
}
