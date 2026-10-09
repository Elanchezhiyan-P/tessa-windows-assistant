using System.ComponentModel;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly AppSettings _appSettings = AppSettings.Load();
    private readonly ToolContext _ctx = new();
    private readonly ToolRegistry _tools;
    private readonly ActionLog _log = new();
    private readonly MemoryStore _memory = new();
    private readonly ChatStore _chat = new();
    private readonly ReminderStore _reminders = new();
    private readonly ReminderService _reminderService;
    private readonly PetController _pets;
    private readonly VoiceService _voice = new();
    private readonly SpeechSynthesizer _speaker = new();
    private readonly DispatcherTimer _thinkingTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private ILlm? _llm;
    private ActiveAppInfo? _activeApp = null; // the foreground app is no longer tracked
    private TaskCompletionSource<bool>? _pendingConfirm;
    private bool _historyOpen = true, _settingsOpen, _positioned, _backdropApplied;
    private int _thinkingStep;

    public MainWindow(SettingsStore settings)
    {
        InitializeComponent();
        _settings = settings;
        _ctx.Window = this;
        _ctx.Settings = _appSettings;
        _ctx.Confirm = ConfirmAsync;
        _tools = ToolRegistry.CreateDefault(_ctx, _memory, _appSettings, _reminders);
        _pets = new PetController(_appSettings);
        Closing += OnClosing;

        ApplyIdentity();
        UpdateMuteButton();

        foreach (var entry in _chat.Entries) Messages.Children.Add(ChatBubbles.Create(entry, animate: false));
        RefreshLayout();
        RefreshChips();
        RefreshModelLabel();

        _thinkingTimer.Tick += (_, _) =>
        {
            _thinkingStep = (_thinkingStep + 1) % 4;
            Status.Text = "Thinking" + new string('.', _thinkingStep);
        };

        _voice.ListeningStarted += () => Dispatcher.Invoke(() =>
        {
            _speaker.SpeakAsyncCancelAll();
            ShowAndFocus();
            Status.Text = "Listening…";
        });
        _voice.Notice += message => Dispatcher.Invoke(() => Status.Text = message);
        _voice.ListeningEnded += () => Dispatcher.Invoke(() => { if (!_thinkingTimer.IsEnabled) Status.Text = ""; });
        _voice.CommandHeard += text => Dispatcher.InvokeAsync(() => SubmitAsync(text, spoken: true));

        // Reminders are checked against the clock every second, with no AI involved.
        _reminderService = new ReminderService(_reminders);
        _reminderService.Due += OnReminderDue;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, _reminderService.Start);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        if (_backdropApplied) return;
        Backdrop.Apply(this, Surface);
        _backdropApplied = true;
    }

    // ---- Public surface used by the tray and hotkeys ---------------------------------------------

    /// <summary>Raised when a reminder fires, so the tray icon can show a balloon even if the window is hidden.</summary>
    public event Action<string, string>? ReminderFired;

    public bool WakeWordEnabled => _voice.WakeWordEnabled;

    public bool SetWakeWord(bool enabled)
    {
        try
        {
            _voice.SetWakeWordEnabled(enabled);
            return true;
        }
        catch (Exception ex)
        {
            AddMessage("error", $"Speech recognition unavailable: {ex.Message}");
            return false;
        }
    }

    public void StartListening()
    {
        try { _voice.BeginListening(); }
        catch (Exception ex) { AddMessage("error", $"Speech recognition unavailable: {ex.Message}"); }
    }

    public void ShowAndFocus()
    {
        var wasHidden = !IsVisible;
        if (wasHidden)
        {
            if (!_positioned) PositionNearTop();
            ApplyBackdrop();
        }

        Show();
        Activate();
        if (_settingsOpen) return;
        Input.Focus();
        if (wasHidden) PlayShowAnimation();
    }

    public void OpenSettings(string? note = null)
    {
        ShowAndFocus();

        ProviderGemini.IsChecked = !_appSettings.UseLocal;
        ProviderLocal.IsChecked = _appSettings.UseLocal;
        LocalUrl.Text = _appSettings.LocalBaseUrl;
        LocalModel.Text = _appSettings.LocalModel;
        LocalVision.Text = _appSettings.LocalVisionModel;
        BuildPetChoices();
        PsAsk.IsChecked = !_appSettings.PowerShellTrusted;
        PrivacyAsk.IsChecked = _appSettings.AskBeforeSendingToCloud;
        PsTrusted.IsChecked = _appSettings.PowerShellTrusted;
        PsAdmin.IsChecked = _appSettings.PowerShellAdmin;
        TabAi.IsChecked = true;
        MuteSwitch.IsChecked = _appSettings.VoiceMuted;
        AboutName.Text = _appSettings.AssistantName;
        AboutVersion.Text = "Version " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        ProfileName.Text = _appSettings.UserName;
        ProfileNick.Text = _appSettings.UserNickname;
        AssistantNameBox.Text = _appSettings.AssistantName;
        VoiceMaleSeg.IsChecked = _appSettings.VoiceGender == "male";
        VoiceFemaleSeg.IsChecked = _appSettings.VoiceGender != "male";
        GeminiKeyBox.Clear();
        ShowKeyState();
        SettingsNote.Text = note ?? "";
        UpdateProviderGroups();

        _settingsOpen = true;
        RefreshLayout();
    }

    // ---- Window behaviour -------------------------------------------------------------------------

    // Closing the window hides it; the tray flyout's Exit is the real quit.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        _speaker.SpeakAsyncCancelAll();
        ResolveConfirm(false);
        _settingsOpen = false;
        RefreshLayout();
        Hide();
    }

    private void PositionNearTop()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.12;
        _positioned = true;
    }

    private void PlayShowAnimation()
    {
        var slide = new TranslateTransform(0, 14);
        Root.RenderTransform = slide;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _settingsOpen = false;
        _historyOpen = !_historyOpen;
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        var showHistory = !_settingsOpen && _historyOpen && Messages.Children.Count > 0;
        Scroll.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        Chips.Visibility = _settingsOpen ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.Content = _historyOpen ? "" : ""; // chevron down = collapse, up = expand
        Dispatcher.BeginInvoke(() => SmoothScroll.ScrollToEnd(Scroll), DispatcherPriority.Background);
    }

    private void RefreshModelLabel() =>
        ModelLabel.Text = _appSettings.UseLocal ? $"Local · {_appSettings.LocalModel}" : "Gemini";

    // ---- Settings -----------------------------------------------------------------------------------

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsOpen) CancelSettings_Click(sender, e);
        else OpenSettings();
    }

    private void AboutLink_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string link)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (PageAi is null || PageYou is null || PageSafety is null || PageAbout is null) return; // fires during InitializeComponent
        var page = TabYou.IsChecked == true ? PageYou : TabSafety.IsChecked == true ? PageSafety : TabAbout.IsChecked == true ? PageAbout : PageAi;
        foreach (var p in new[] { PageAi, PageYou, PageSafety, PageAbout }) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void Provider_Changed(object sender, RoutedEventArgs e) => UpdateProviderGroups();

    private void UpdateProviderGroups()
    {
        if (GeminiGroup is null || LocalGroup is null) return; // fires during InitializeComponent
        var local = ProviderLocal.IsChecked == true;
        GeminiGroup.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalGroup.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PresetOllama_Click(object sender, RoutedEventArgs e) => LocalUrl.Text = "http://localhost:11434/v1";
    private void PresetLmStudio_Click(object sender, RoutedEventArgs e) => LocalUrl.Text = "http://localhost:1234/v1";
    private void PresetLlamaCpp_Click(object sender, RoutedEventArgs e) => LocalUrl.Text = "http://localhost:8080/v1";

    private async void Detect_Click(object sender, RoutedEventArgs e)
    {
        DetectButton.IsEnabled = false;
        LocalStatus.Text = "Looking for models…";
        try
        {
            var models = await LocalLlmClient.ListModelsAsync(LocalUrl.Text.Trim());
            if (models.Count == 0)
            {
                LocalStatus.Text = "The server is running but has no models. Pull one, e.g. 'ollama pull qwen2.5:7b'.";
                return;
            }

            // Prefer a Qwen text model for chat and a Qwen vision model for screenshots.
            var chat = models.FirstOrDefault(m => m.Contains("qwen", StringComparison.OrdinalIgnoreCase)
                                                  && !m.Contains("vl", StringComparison.OrdinalIgnoreCase)) ?? models[0];
            var vision = models.FirstOrDefault(m => m.Contains("qwen", StringComparison.OrdinalIgnoreCase)
                                                    && m.Contains("vl", StringComparison.OrdinalIgnoreCase));

            if (!models.Contains(LocalModel.Text.Trim())) LocalModel.Text = chat;
            if (vision is not null && string.IsNullOrWhiteSpace(LocalVision.Text)) LocalVision.Text = vision;
            LocalStatus.Text = $"Found {models.Count} model(s): {string.Join(", ", models.Take(8))}";
        }
        catch (LlmException ex)
        {
            LocalStatus.Text = ex.Message;
        }
        finally
        {
            DetectButton.IsEnabled = true;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var local = ProviderLocal.IsChecked == true;

        if (local)
        {
            if (!Uri.TryCreate(LocalUrl.Text.Trim(), UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(LocalModel.Text))
            {
                LocalStatus.Text = "Enter the server URL and a model name (press Detect to see what's installed).";
                return;
            }
            _appSettings.LocalBaseUrl = LocalUrl.Text.Trim();
            _appSettings.LocalModel = LocalModel.Text.Trim();
            _appSettings.LocalVisionModel = LocalVision.Text.Trim();
        }
        else
        {
            if (GeminiKeyBox.Password.Length > 0) _settings.SaveApiKey(GeminiKeyBox.Password.Trim());
            if (string.IsNullOrEmpty(_settings.LoadApiKey()))
            {
                GeminiKeyState.Text = "Paste an API key to use Gemini, or switch to Local.";
                return;
            }
        }

        _appSettings.PowerShellTrusted = PsTrusted.IsChecked == true;
        _appSettings.PowerShellAdmin = PsAdmin.IsChecked == true;
        _appSettings.Pet = SelectedPetId();
        _appSettings.AskBeforeSendingToCloud = PrivacyAsk.IsChecked == true;
        _appSettings.ShareActiveApp = false;
        _appSettings.Provider = local ? "local" : "gemini";
        _appSettings.SetProfile(ProfileName.Text, ProfileNick.Text, AssistantNameBox.Text, VoiceMaleSeg.IsChecked == true);
        _appSettings.VoiceMuted = MuteSwitch.IsChecked == true;
        if (_appSettings.VoiceMuted) _speaker.SpeakAsyncCancelAll();
        UpdateMuteButton();
        _appSettings.Save();
        ApplyIdentity();
        _llm = null; // rebuild with the new settings on the next request
        _ctx.Llm = null;

        _settingsOpen = false;
        RefreshLayout();
        RefreshModelLabel();
        Status.Text = "Settings saved.";
        Input.Focus();
    }

    private void CancelSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsOpen = false;
        RefreshLayout();
        Input.Focus();
    }

    // ---- Suggestions --------------------------------------------------------------------------------

    private void RefreshChips()
    {
        HeaderApp.Text = _activeApp is null ? "" : "·  " + ActiveApp.FriendlyName(_activeApp);
        Chips.Children.Clear();
        foreach (var suggestion in ActiveApp.Suggestions(_activeApp))
        {
            var chip = new Button { Content = suggestion.Label, Style = (Style)FindResource("Chip") };
            chip.Click += async (_, _) => await SubmitAsync(suggestion.Label, spoken: false, sendText: suggestion.Prompt);
            Chips.Children.Add(chip);
        }
    }

    // ---- Chat ---------------------------------------------------------------------------------------

    private void AddMessage(string role, string text)
    {
        var entry = new ChatEntry(role, text, DateTime.Now);
        _chat.Append(entry);
        Messages.Children.Add(ChatBubbles.Create(entry, animate: true));
        _historyOpen = true;
        _settingsOpen = false;
        RefreshLayout();
    }

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _speaker.SpeakAsyncCancelAll();
        _llm?.ClearHistory();
        LlmClientBase.DeleteSavedContexts();
        _chat.Clear();
        Messages.Children.Clear();
        RefreshLayout();
        Input.Focus();
    }

    // Builds the model client for the chosen provider, or opens Settings if it isn't usable yet.
    private ILlm? EnsureClient()
    {
        if (_llm is not null) return _llm;

        ILlm client;
        if (_appSettings.UseLocal)
        {
            client = new LocalLlmClient(_appSettings, _tools, ConfirmAsync, _log, _memory, SharedActiveApp);
        }
        else
        {
            var key = _settings.LoadApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                OpenSettings("Add your Gemini API key, or switch to a local Qwen model.");
                return null;
            }
            client = new GeminiClient(key, _tools, ConfirmAsync, _log, _memory, SharedActiveApp);
        }

        client.ToolCalled += call => Dispatcher.Invoke(() => AddMessage("tool", call));
        client.ToolFinished += result => Dispatcher.Invoke(() => AddMessage("tool", result));
        _ctx.Llm = client;
        _llm = client;
        return client;
    }

    // What the AI is told about the foreground app; nothing when the user turned that off.
    private ActiveAppInfo? SharedActiveApp() => _appSettings.ShareActiveApp ? _activeApp : null;

    // ---- Approvals ----------------------------------------------------------------------------------

    private Task<bool> ConfirmAsync(string description)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Invoke(() =>
        {
            ResolveConfirm(false); // never leave an earlier request hanging
            _pendingConfirm = tcs;
            ConfirmText.Text = description;
            ConfirmCard.Visibility = Visibility.Visible;
            ShowAndFocus();
        });
        return tcs.Task;
    }

    private void ResolveConfirm(bool allowed)
    {
        ConfirmCard.Visibility = Visibility.Collapsed;
        var pending = _pendingConfirm;
        _pendingConfirm = null;
        pending?.TrySetResult(allowed);
    }

    private void Allow_Click(object sender, RoutedEventArgs e) => ResolveConfirm(true);
    private void Deny_Click(object sender, RoutedEventArgs e) => ResolveConfirm(false);

    // ---- Input --------------------------------------------------------------------------------------

    private void Input_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Send_Click(sender, e);
        else if (e.Key == Key.Escape) Close();
    }

    private void Mic_Click(object sender, RoutedEventArgs e) => StartListening();

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        Input.Clear();
        await SubmitAsync(text, spoken: false);
    }

    /// <param name="display">What appears in the chat.</param>
    /// <param name="sendText">What is sent to the model, when it differs from what is displayed.</param>
    private async Task SubmitAsync(string display, bool spoken, string? sendText = null)
    {
        // Reminders are handled right here: no model, no internet, no quota.
        if (LocalCommands.TryHandle(sendText ?? display, _reminders) is { } localReply)
        {
            AddMessage("user", display);
            AddMessage("assistant", localReply);
            if (spoken) Speak(localReply);
            return;
        }

        var client = EnsureClient();
        if (client is null) return;

        AddMessage("user", display);
        SetBusy(true);
        try
        {
            var reply = await client.SendAsync(sendText ?? display);
            AddMessage("assistant", reply.Length > 0 ? reply : "(no reply)");
            if (spoken) Speak(reply);
        }
        catch (LlmException ex)
        {
            AddMessage("error", ex.Message);
        }
        catch (Exception ex)
        {
            AddMessage("error", $"Something went wrong: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private FrameworkElement? _typing;

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = Input.IsEnabled = !busy;
        if (busy)
        {
            // Soft pulsing dots in the chat tell the user she is working, instead of a line of text.
            _typing = TypingIndicator.Create();
            Messages.Children.Add(_typing);
            _historyOpen = true;
            _settingsOpen = false;
            RefreshLayout();
        }
        else
        {
            if (_typing is not null) Messages.Children.Remove(_typing);
            _typing = null;
            Status.Text = "";
            Input.Focus();
        }
    }

    private void OnReminderDue(Reminder reminder, bool missed)
    {
        var text = $"⏰ **Reminder:** {reminder.Message}";
        if (missed) text += $"\n\n(missed: it was due {reminder.Due:ddd d MMM, h:mm tt})";

        System.Media.SystemSounds.Exclamation.Play();
        AddMessage("assistant", text);

        // The chosen character delivers it; with none chosen, or a full-screen app in front, fall back to the plain pop-up.
        var delivered = _pets.Deliver(reminder.Message, snooze: () => Snooze(reminder.Message), done: null);
        if (!delivered && !PetController.IsFullScreenAppRunning()) ShowAndFocus();

        Speak($"Reminder: {reminder.Message}");
        ReminderFired?.Invoke($"{_appSettings.AssistantName} reminder", reminder.Message);
    }

    private void Snooze(string message)
    {
        const int minutes = 5;
        _reminders.Add(message, DateTime.Now.AddMinutes(minutes));
        AddMessage("assistant", $"Snoozed for {minutes} minutes: {message}");
    }

    // ---- Reminder character -------------------------------------------------------------------------

    public void PreviewPet(string id) => _pets.Preview(id);

    private void BuildPetChoices()
    {
        PetCatalog.Reload(); // pick up packs the user just dropped in
        PetChoices.Children.Clear();

        var choices = PetCatalog.All().Select(p => (p.Id, p.Name)).Append(("none", "None"));
        foreach (var (id, name) in choices)
        {
            PetChoices.Children.Add(new RadioButton
            {
                Content = name,
                Tag = id,
                GroupName = "pet",
                Style = (Style)FindResource("Segment"),
                IsChecked = id.Equals(_appSettings.Pet, StringComparison.OrdinalIgnoreCase)
            });
        }
        PetHint.Text = $"Add your own: put a folder with pet.json and sheet.png in {PetCatalog.UserFolder}";
    }

    private string SelectedPetId() =>
        PetChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "none";

    private void PreviewPet_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedPetId();
        if (id != "none") _pets.Preview(id);
    }

    /// <summary>Raised when the assistant's name changes, so the tray icon and menu can follow.</summary>
    public event Action? IdentityChanged;

    /// <summary>Applies her name, wake phrase and voice from the settings.</summary>
    private void ApplyIdentity()
    {
        Title = _appSettings.AssistantName;
        HeaderText.Text = _appSettings.AssistantName;
        _voice.SetWakePhrase(_appSettings.WakePhrase);
        try { _speaker.SelectVoiceByHints(_appSettings.VoiceGender == "male" ? System.Speech.Synthesis.VoiceGender.Male : System.Speech.Synthesis.VoiceGender.Female); }
        catch { /* keep the default voice */ }
        IdentityChanged?.Invoke();
    }

    /// <summary>The first message after the welcome screen.</summary>
    public void Welcome()
    {
        var who = _appSettings.CallUser.Length > 0 ? $"Hi {_appSettings.CallUser}! " : "Hi! ";
        AddMessage("assistant", $"{who}I'm {_appSettings.AssistantName}. Ask me anything about your PC, or say “remind me in 10 minutes to stretch”. " +
                                $"Turn on the wake word in the tray menu and you can just say “{_appSettings.WakeLabel}”.");
        ShowAndFocus();
    }

    private void ShowKeyState()
    {
        GeminiKeyState.Text = _settings.DescribeKey() is { } saved
            ? $"Saved key: {saved.Masked}  ({saved.Source}). Leave blank to keep it, or paste a new key and Save to replace it."
            : "No key saved yet.";
        RemoveKeyButton.IsEnabled = _settings.DescribeKey() is { Source: "encrypted on this PC" };
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        _settings.RemoveApiKey();
        GeminiKeyBox.Clear();
        _llm = null; // the old key must not stay in use
        _ctx.Llm = null;
        ShowKeyState();
        if (_settings.DescribeKey() is not null)
            GeminiKeyState.Text += "  The saved key was removed, but a GEMINI_API_KEY environment variable is still set.";
        else
            GeminiKeyState.Text = "Key removed. Paste a new one to use Gemini, or switch to Local.";
    }

    private void UpdateMuteButton()
    {
        MuteButton.Content = _appSettings.VoiceMuted ? "\uE74F" : "\uE767"; // muted speaker / speaker
        MuteButton.ToolTip = _appSettings.VoiceMuted ? "Unmute her voice" : "Mute her voice";
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.VoiceMuted = !_appSettings.VoiceMuted;
        _appSettings.Save();
        if (_appSettings.VoiceMuted) _speaker.SpeakAsyncCancelAll(); // stop mid-sentence
        UpdateMuteButton();
        Status.Text = _appSettings.VoiceMuted ? "Voice muted." : "Voice on.";
    }

    private void Speak(string text)
    {
        if (_appSettings.VoiceMuted) return;
        // Strip markdown marks so the voice doesn't read out asterisks.
        var clean = text.Replace("*", "").Replace("`", "").Replace("#", "");
        _speaker.SpeakAsyncCancelAll();
        _speaker.SpeakAsync(clean);
    }
}
