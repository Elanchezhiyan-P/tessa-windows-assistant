using System.Speech.Recognition;


namespace WinCompanion.Services;

/// <summary>
/// Speech input: an optional always-on wake phrase (for example "Hey Tessa") and a dictation window that opens after the
/// wake phrase or on demand (push-to-talk hotkey). The wake phrase uses the small built-in recogniser (it only has to
/// match one phrase). What you say after it goes to Windows' online speech recognition, the same service as Windows voice
/// typing, which understands accents and everyday speech far better than the old built-in dictation. If that is switched
/// off in Windows, the old dictation is used instead and a hint is shown. Events fire on a background thread.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private string _wakePhrase = "hey tessa";
    private const float WakeConfidence = 0.7f;
    private static readonly TimeSpan DictationTimeout = TimeSpan.FromSeconds(8);

    private readonly object _gate = new();
    private SpeechRecognitionEngine? _engine;
    private Grammar? _wakeGrammar, _dictationGrammar;
    private System.Threading.Timer? _timeout;
    private bool _running, _listening, _wakeEnabled;
    private bool _onlineUnavailable;     // the modern recogniser failed once: use the old dictation instead
    private bool _noticeShown;
    private int _session;                // identifies the current dictation window, so a late result from an old one is ignored

    /// <summary>A hint for the user (for example how to switch on online speech recognition).</summary>
    public event Action<string>? Notice;

    /// <summary>The dictation window opened (wake phrase heard or hotkey pressed).</summary>
    public event Action? ListeningStarted;
    /// <summary>The dictation window closed, with or without a result.</summary>
    public event Action? ListeningEnded;
    /// <summary>A spoken command was recognized.</summary>
    public event Action<string>? CommandHeard;

    public bool WakeWordEnabled
    {
        get { lock (_gate) return _wakeEnabled; }
    }

    /// <summary>Changes the phrase that wakes her (e.g. "hey tessa"), also while listening.</summary>
    public void SetWakePhrase(string phrase)
    {
        lock (_gate)
        {
            if (phrase == _wakePhrase) return;
            _wakePhrase = phrase;
            if (_engine is not null && _wakeGrammar is not null)
            {
                _engine.UnloadGrammar(_wakeGrammar);
                _wakeGrammar = new Grammar(new GrammarBuilder(_wakePhrase)) { Name = "wake" };
                _engine.LoadGrammar(_wakeGrammar);
            }
            Apply();
        }
    }

    public void SetWakeWordEnabled(bool enabled)
    {
        lock (_gate)
        {
            _wakeEnabled = enabled;
            Apply();
        }
    }

    /// <summary>Open the dictation window now, skipping the wake phrase.</summary>
    public void BeginListening()
    {
        lock (_gate)
        {
            if (_listening) return;
            _listening = true;
            _session++;
            _timeout = new System.Threading.Timer(_ => EndListening(), null,
                _onlineUnavailable ? DictationTimeout : TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
            Apply();
        }
        ListeningStarted?.Invoke();
        if (!_onlineUnavailable) _ = ListenOnlineAsync(_session);
    }

    private static string Explain(Exception ex)
    {
        var code = $"0x{ex.HResult:X8}";
        return unchecked((uint)ex.HResult) switch
        {
            0x80045509 => "Turn on Online speech recognition for clearer dictation: Windows Settings, Privacy & security, Speech. Using the basic recogniser for now.",
            0x80070005 => "Windows blocked the microphone for desktop apps: Settings, Privacy & security, Microphone, turn on \"Let desktop apps access your microphone\". Using the basic recogniser for now.",
            0x8004503A => "Your Windows speech language isn't installed: Settings, Time & language, Speech. Using the basic recogniser for now.",
            0x80131501 or 0x80004005 => $"Windows speech recognition isn't ready ({code}); check the microphone and Online speech recognition in Settings, Privacy & security. Using the basic recogniser.",
            _ => $"Windows speech recognition failed ({code}: {ex.Message.Trim()}). Using the basic recogniser. Details are in speech.log in her data folder."
        };
    }

    private static void LogProblem(Exception ex)
    {
        try { File.AppendAllText(AppPaths.FileIn("speech.log"), $"{DateTime.Now:s}  0x{ex.HResult:X8}  {ex.GetType().Name}: {ex.Message}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    /// <summary>One dictation with Windows' online recogniser. On any problem, falls back to the built-in dictation.</summary>
    private async Task ListenOnlineAsync(int session)
    {
        string? text = null;
        Windows.Media.SpeechRecognition.SpeechRecognizer? recognizer = null;
        try
        {
            recognizer = new Windows.Media.SpeechRecognition.SpeechRecognizer();
            recognizer.Constraints.Add(new Windows.Media.SpeechRecognition.SpeechRecognitionTopicConstraint(Windows.Media.SpeechRecognition.SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success) throw new InvalidOperationException(compiled.Status.ToString());

            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(7);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.4);
            var result = await recognizer.RecognizeAsync();
            if (result.Status == Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success) text = result.Text?.Trim();
        }
        catch (Exception ex)
        {
            // 0x80045509: "Online speech recognition" is switched off in Windows privacy settings.
            lock (_gate) { _onlineUnavailable = true; }
            LogProblem(ex);
            if (!_noticeShown)
            {
                _noticeShown = true;
                Notice?.Invoke(Explain(ex));
            }
            lock (_gate) { if (_listening && _session == session) Apply(); } // start the fallback dictation now
            return;
        }
        finally
        {
            recognizer?.Dispose();
        }

        bool current;
        lock (_gate) { current = _listening && _session == session; }
        if (!current) return;
        EndListening();
        if (!string.IsNullOrEmpty(text)) CommandHeard?.Invoke(text);
    }

    private void EndListening()
    {
        lock (_gate)
        {
            if (!_listening) return;
            _listening = false;
            _timeout?.Dispose();
            _timeout = null;
            Apply();
        }
        ListeningEnded?.Invoke();
    }

    // Must be called with _gate held. Opens the mic only while something needs it.
    private void Apply()
    {
        var shouldRun = _wakeEnabled || (_listening && _onlineUnavailable);
        if (shouldRun && !_running)
        {
            EnsureEngine();
            _engine!.RecognizeAsync(RecognizeMode.Multiple);
            _running = true;
        }
        else if (!shouldRun && _running)
        {
            _engine!.RecognizeAsyncCancel();
            _running = false;
        }

        if (_wakeGrammar is not null) _wakeGrammar.Enabled = _wakeEnabled && !_listening;
        if (_dictationGrammar is not null) _dictationGrammar.Enabled = _listening && _onlineUnavailable;
    }

    private void EnsureEngine()
    {
        if (_engine is not null) return;

        var engine = new SpeechRecognitionEngine();
        engine.SetInputToDefaultAudioDevice();

        _wakeGrammar = new Grammar(new GrammarBuilder(_wakePhrase)) { Name = "wake" };
        _dictationGrammar = new DictationGrammar { Name = "dictation", Enabled = false };
        engine.LoadGrammar(_wakeGrammar);
        engine.LoadGrammar(_dictationGrammar);
        engine.SpeechRecognized += OnRecognized;
        _engine = engine;
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Grammar.Name == "wake")
        {
            if (e.Result.Confidence >= WakeConfidence) BeginListening();
            return;
        }

        var text = e.Result.Text.Trim();
        EndListening();
        if (text.Length > 0) CommandHeard?.Invoke(text);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timeout?.Dispose();
            _engine?.Dispose();
            _engine = null;
        }
    }
}
