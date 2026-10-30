using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// Voice and speed, colour themes, quiet hours, low-disk and battery warnings, and the list of what she remembers.
public partial class MainWindow
{
    private bool _loadingPersonalization;

    private bool QuietNow => QuietHours.IsQuietNow(_appSettings, DateTime.Now);

    /// <summary>Fills the Look and You tabs from the saved settings. Called whenever Settings opens.</summary>
    private void LoadPersonalization()
    {
        _loadingPersonalization = true;
        try
        {
            PopulateVoices();
            VoiceSpeed.Value = Math.Clamp(_appSettings.VoiceRate, -6, 6);
            UpdateVoiceSpeedLabel();

            BuildThemeChoices();
            AccentBox.Text = _appSettings.AccentColor;
            ThemeHint.Text = "";

            QuietSwitch.IsChecked = _appSettings.QuietHours;
            QuietStartBox.Text = _appSettings.QuietStart;
            QuietEndBox.Text = _appSettings.QuietEnd;
            QuietOwnSwitch.IsChecked = _appSettings.QuietOwnReminders;
            HealthSwitch.IsChecked = _appSettings.HealthAlerts;
            HealthNow.Text = HealthReport();

            _memoryClearArmed = false;
            RefreshMemoryList();
        }
        finally { _loadingPersonalization = false; }
    }

    /// <summary>Checks the fields that can be typed wrongly, before anything is saved. Shows what to fix.</summary>
    private bool PersonalizationValid()
    {
        if (!QuietHours.TryParseTime(QuietStartBox.Text, out _) || !QuietHours.TryParseTime(QuietEndBox.Text, out _))
        {
            TabLook.IsChecked = true;
            SettingsNote.Text = "Quiet hours need times like 22:00 and 07:00.";
            return false;
        }
        if (AccentBox.Text.Trim().Length > 0 && !ThemeCatalog.TryParseAccent(AccentBox.Text, out _))
        {
            TabLook.IsChecked = true;
            SettingsNote.Text = "The accent colour needs six hex digits, like #FF8800.";
            return false;
        }
        return true;
    }

    private void SavePersonalization()
    {
        _appSettings.VoiceName = SelectedVoiceName();
        _appSettings.VoiceRate = (int)VoiceSpeed.Value;

        _appSettings.Theme = SelectedThemeKey();
        _appSettings.AccentColor = ThemeCatalog.TryParseAccent(AccentBox.Text, out var accent)
            ? $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}" : "";

        QuietHours.TryParseTime(QuietStartBox.Text, out var start);
        QuietHours.TryParseTime(QuietEndBox.Text, out var end);
        _appSettings.QuietHours = QuietSwitch.IsChecked == true;
        _appSettings.QuietStart = QuietHours.Format(start);
        _appSettings.QuietEnd = QuietHours.Format(end);
        _appSettings.QuietOwnReminders = QuietOwnSwitch.IsChecked == true;
        _appSettings.HealthAlerts = HealthSwitch.IsChecked == true;

        ApplyTheme(_appSettings.Theme, _appSettings.AccentColor);
    }

    /// <summary>Puts back what was saved after the user previewed a theme or voice and then cancelled.</summary>
    private void RevertPersonalization()
    {
        ApplyTheme(_appSettings.Theme, _appSettings.AccentColor);
        ApplyVoice();
    }

    // ---- Voice --------------------------------------------------------------------------------------

    private void PopulateVoices()
    {
        VoiceBox.Items.Clear();
        VoiceBox.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = "" });
        try
        {
            foreach (var voice in _speaker.GetInstalledVoices().Where(v => v.Enabled))
            {
                var info = voice.VoiceInfo;
                VoiceBox.Items.Add(new ComboBoxItem { Content = $"{info.Name} ({info.Culture.Name}, {info.Gender})", Tag = info.Name });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* no speech engine */ }

        var saved = VoiceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == _appSettings.VoiceName);
        VoiceBox.SelectedItem = saved ?? VoiceBox.Items[0];
    }

    private string SelectedVoiceName() => (VoiceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private static string SpeedText(int rate) => rate == 0 ? "normal" : rate < 0 ? $"slower ({rate})" : $"faster (+{rate})";

    private void UpdateVoiceSpeedLabel() => VoiceSpeedLabel.Text = $"Speed: {SpeedText((int)VoiceSpeed.Value)}";

    private void VoiceSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VoiceSpeedLabel is not null) UpdateVoiceSpeedLabel();
    }

    /// <summary>Selects the voice and speed. An empty name falls back to a voice of the chosen gender.</summary>
    private void ApplyVoice(string? name = null, int? rate = null)
    {
        name ??= _appSettings.VoiceName;
        try
        {
            if (name.Length > 0 && _speaker.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Name == name)) _speaker.SelectVoice(name);
            else _speaker.SelectVoiceByHints(_appSettings.VoiceGender == "male" ? VoiceGender.Male : VoiceGender.Female);
        }
        catch { /* keep the default voice */ }
        try { _speaker.Rate = Math.Clamp(rate ?? _appSettings.VoiceRate, -10, 10); }
        catch (ArgumentOutOfRangeException) { }
    }

    private void PreviewVoice_Click(object sender, RoutedEventArgs e)
    {
        _speaker.SpeakAsyncCancelAll();
        ApplyVoice(SelectedVoiceName(), (int)VoiceSpeed.Value);
        _speaker.SpeakAsync($"Hi, I'm {AppSettings.CleanName(AssistantNameBox.Text, _appSettings.AssistantName)}. This is how I sound.");
    }

    // ---- Themes -------------------------------------------------------------------------------------

    private void BuildThemeChoices()
    {
        ThemeChoices.Children.Clear();
        foreach (var theme in ThemeCatalog.All)
        {
            var choice = new RadioButton
            {
                Content = theme.Name,
                Tag = theme.Key,
                GroupName = "theme",
                Style = (Style)FindResource("Segment"),
                IsChecked = theme.Key.Equals(ThemeCatalog.Find(_appSettings.Theme).Key, StringComparison.OrdinalIgnoreCase)
            };
            choice.Checked += (_, _) => PreviewTheme();
            ThemeChoices.Children.Add(choice);
        }
    }

    private string SelectedThemeKey() =>
        ThemeChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "midnight";

    private void Accent_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingPersonalization || ThemeChoices is null) return;
        var text = AccentBox.Text.Trim();
        if (text.Length == 0 || ThemeCatalog.TryParseAccent(text, out _)) PreviewTheme();
    }

    private void PreviewTheme()
    {
        if (_loadingPersonalization) return;
        ApplyTheme(SelectedThemeKey(), AccentBox.Text);
    }

    private void ApplyTheme(string key, string? accent)
    {
        var live = ThemeCatalog.Apply(Application.Current.Resources, ThemeCatalog.Find(key), accent);
        Backdrop.Retint(Surface);
        if (ThemeHint is not null) ThemeHint.Text = live ? "" : "The new colours finish applying the next time you start the app.";
    }

    /// <summary>Applies the saved theme before any window is shown.</summary>
    public static void ApplySavedTheme(AppSettings settings) =>
        ThemeCatalog.Apply(Application.Current.Resources, ThemeCatalog.Find(settings.Theme), settings.AccentColor);

    // ---- Warnings -----------------------------------------------------------------------------------

    private readonly HealthRules _health = new();
    private DispatcherTimer? _healthTimer;

    private void StartHealthMonitor()
    {
        // A first look shortly after start, then every few minutes.
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _healthTimer.Tick += (_, _) =>
        {
            _healthTimer.Interval = TimeSpan.FromMinutes(5);
            CheckHealth();
        };
        _healthTimer.Start();
    }

    private void CheckHealth()
    {
        if (!_appSettings.HealthAlerts) return;
        var (battery, onBattery) = HealthRules.ReadBattery();
        foreach (var warning in _health.Check(SystemInfo.ReadDrives(), battery, onBattery)) ShowAlert(warning);
    }

    private string HealthReport()
    {
        var (battery, onBattery) = HealthRules.ReadBattery();
        return HealthRules.Describe(SystemInfo.ReadDrives(), battery, onBattery, _appSettings.HealthAlerts);
    }

    /// <summary>A warning always lands in the chat; outside quiet hours it is also announced like a reminder.</summary>
    private void ShowAlert(string text)
    {
        AddMessage("assistant", "⚠️ " + text);
        if (QuietNow) return;

        System.Media.SystemSounds.Exclamation.Play();
        var delivered = _pets.Deliver(text, snooze: () => Snooze(text), done: null);
        if (!delivered && !PetController.IsFullScreenAppRunning()) ShowAndFocus();
        Speak(text);
        ReminderFired?.Invoke($"{_appSettings.AssistantName} warning", text);
    }

    // ---- What she remembers -------------------------------------------------------------------------

    private bool _memoryClearArmed;

    private void RefreshMemoryList()
    {
        MemoryList.Children.Clear();
        var items = _memory.List();
        if (items.Count == 0)
        {
            MemoryList.Children.Add(new TextBlock
            {
                Text = "Nothing yet. Tell her “remember that my project folder is D:\\Work”, or add a fact below.",
                Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var item in items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = item.Text,
                Foreground = (System.Windows.Media.Brush)FindResource("Fg"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Saved {item.Added:d MMM yyyy}"
            };
            var delete = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = "\uE711",
                Width = 26,
                Height = 26,
                FontSize = 11,
                ToolTip = "Forget this",
                Margin = new Thickness(6, 0, 0, 0)
            };
            var id = item.Id;
            delete.Click += (_, _) =>
            {
                _memory.Delete(id);
                RefreshMemoryList();
            };
            Grid.SetColumn(delete, 1);
            row.Children.Add(text);
            row.Children.Add(delete);
            MemoryList.Children.Add(row);
        }
    }

    private void MemoryAdd_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryBox.Text.Trim().Length == 0) return;
        SettingsNote.Text = _memory.Remember(MemoryBox.Text);
        MemoryBox.Clear();
        RefreshMemoryList();
    }

    private void MemoryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        MemoryAdd_Click(sender, e);
        e.Handled = true;
    }

    // Forgetting everything needs a second click, since it can't be undone.
    private void MemoryClear_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (!_memoryClearArmed)
        {
            _memoryClearArmed = true;
            button.Content = "Click again to forget everything";
            return;
        }
        _memoryClearArmed = false;
        button.Content = "Forget everything";
        SettingsNote.Text = $"Forgot {_memory.Clear()} thing(s).";
        RefreshMemoryList();
    }

    // ---- Tour ---------------------------------------------------------------------------------------

    private void Tour_Click(object sender, RoutedEventArgs e) => ShowTour();

    public void ShowTour() => new TourWindow(_appSettings).Show();
}
