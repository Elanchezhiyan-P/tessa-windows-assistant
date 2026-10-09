using System.Windows;
using WinCompanion.Services;

namespace WinCompanion;

/// <summary>First-run welcome: the user's name, what to call them, what to call her (her wake word) and her voice.</summary>
public partial class SetupWindow : Window
{
    private readonly AppSettings _settings;
    private bool _done, _nickEdited;

    public SetupWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        AssistantBox.Text = settings.AssistantName;
        UserNameBox.Text = settings.UserName;
        NickBox.Text = settings.UserNickname;
        VoiceFemale.IsChecked = settings.VoiceGender != "male";
        VoiceMale.IsChecked = settings.VoiceGender == "male";
        UpdateHint();

        // Until the user types their own short name, suggest the first word of their name.
        NickBox.PreviewKeyDown += (_, _) => _nickEdited = true;
        UserNameBox.TextChanged += (_, _) =>
        {
            if (!_nickEdited) NickBox.Text = UserNameBox.Text.Trim().Split(' ', 2)[0];
        };
        AssistantBox.TextChanged += (_, _) => UpdateHint();
        Loaded += (_, _) => UserNameBox.Focus();
        Closed += (_, _) => { if (!_done) Finish(skip: true); };
    }

    private void UpdateHint()
    {
        var name = AppSettings.CleanName(AssistantBox.Text, "Tessa");
        WakeHint.Text = $"Say “Hey {name}” to wake me (turn on the wake word from the tray menu).";
    }

    private void Finish(bool skip)
    {
        _done = true;
        if (!skip)
            _settings.SetProfile(UserNameBox.Text, NickBox.Text, AssistantBox.Text, VoiceMale.IsChecked == true);
        _settings.SetupDone = true;
        _settings.Save();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        Finish(skip: false);
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Finish(skip: true);
        Close();
    }
}
