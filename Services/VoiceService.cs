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

    /// <summary>Set by the window: whether Gemini can transcribe right now, and how.</summary>
    public Func<bool>? GeminiSpeechAvailable { get; set; }
    public Func<byte[], Task<string?>>? GeminiTranscribe { get; set; }

    /// <summary>The language to listen in ("en-IN"...), or empty for the Windows default.</summary>
    public string SpeechLanguage { get; set; } = "";

    /// <summary>How loud you are right now (0 to about 0.3), many times a second while she records. Drives the listening animation.</summary>
    public event Action<double>? Level;

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
                _onlineUnavailable ? DictationTimeout : TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
            Apply();
        }
        ListeningStarted?.Invoke();
        if (!_onlineUnavailable) _ = ListenOnlineAsync(_session);
    }

    /// <summary>Records the phrase ourselves and has Gemini transcribe it. Returns false when the caller should try Windows' recogniser instead.</summary>
    private async Task<bool> ListenWithGeminiAsync(int session)
    {
        MicRecorder.Recording recording;
        try
        {
            recording = await MicRecorder.RecordUtteranceAsync(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(15),
                () => { lock (_gate) return !_listening || _session != session; }, rms => Level?.Invoke(rms));
        }
        catch (Exception ex) { LogProblem(ex); return false; }

        Log($"gemini-mode outcome={recording.Outcome} peak={recording.Peak:F3} seconds={recording.Seconds:F1} {recording.Detail}");
        if (recording.Outcome.StartsWith("error")) return false; // no usable microphone path: let Windows' recogniser try

        bool current;
        lock (_gate) { current = _listening && _session == session; }
        if (!current) return true;

        if (recording.Wav is null)
        {
            EndListening();
            Notice?.Invoke(recording.Peak < 0.01
                ? "I can barely hear the microphone. Raise the input level in Windows Settings, System, Sound, Input."
                : "I didn't hear anything. Check that the right microphone is selected in Windows Settings, System, Sound, Input.");
            return true;
        }

        string? text;
        try
        {
            Notice?.Invoke("Understanding…");
            text = await GeminiTranscribe!(recording.Wav);
        }
        catch (Exception ex)
        {
            Log($"gemini transcription failed: {ex.Message}");
            EndListening();
            Notice?.Invoke("Couldn't reach Gemini to understand that (" + ex.Message + "). Try again.");
            return true;
        }

        text = text?.Trim().Trim('"', '“', '”').Trim();
        EndListening();
        if (string.IsNullOrEmpty(text) || text.Equals("[none]", StringComparison.OrdinalIgnoreCase))
        {
            Notice?.Invoke("I didn't catch that. Try again, a little closer to the microphone.");
            return true;
        }
        CommandHeard?.Invoke(text);
        return true;
    }

    private Windows.Media.SpeechRecognition.SpeechRecognizer CreateRecognizer()
    {
        var tag = SpeechLanguage.Trim();
        if (tag.Length > 0)
        {
            var supported = Windows.Media.SpeechRecognition.SpeechRecognizer.SupportedTopicLanguages
                .FirstOrDefault(l => l.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (supported is not null) return new Windows.Media.SpeechRecognition.SpeechRecognizer(supported);
            Notice?.Invoke($"Speech language \"{tag}\" isn't available on this PC, so I'm using Windows' default. Add it under Settings, Time & language, Language & region.");
        }
        return new Windows.Media.SpeechRecognition.SpeechRecognizer();
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
        if (GeminiSpeechAvailable?.Invoke() == true && GeminiTranscribe is not null && await ListenWithGeminiAsync(session)) return;

        string? text = null;
        var lowConfidence = false;
        var nothingHeard = false;
        Windows.Media.SpeechRecognition.SpeechRecognizer? recognizer = null;
        try
        {
            recognizer = CreateRecognizer();
            recognizer.Constraints.Add(new Windows.Media.SpeechRecognition.SpeechRecognitionTopicConstraint(Windows.Media.SpeechRecognition.SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success) throw new InvalidOperationException(compiled.Status.ToString());

            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(7);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.4);
            var result = await recognizer.RecognizeAsync();
            Log($"status={result.Status} confidence={result.Confidence} heard={(string.IsNullOrWhiteSpace(result.Text) ? "nothing" : "text")}");
            switch (result.Status)
            {
                case Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success:
                    break;
                case Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.TimeoutExceeded:
                case Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.PauseLimitExceeded:
                case Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.UserCanceled:
                case Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Unknown:
                    nothingHeard = true;
                    break;
                default:
                    // Microphone unavailable, no network, unsupported language...: not a normal "silence", so switch to the basic recogniser.
                    throw new InvalidOperationException("Windows speech recognition returned " + result.Status);
            }
            if (result.Status == Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success
                && result.Confidence is Windows.Media.SpeechRecognition.SpeechRecognitionConfidence.High
                    or Windows.Media.SpeechRecognition.SpeechRecognitionConfidence.Medium)
                text = result.Text?.Trim();
            else if (result.Status == Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success)
                lowConfidence = true;
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
        else if (lowConfidence) Notice?.Invoke("I didn't catch that clearly. Try again, a little closer to the microphone.");
        else if (nothingHeard) Notice?.Invoke("I didn't hear anything. Check that the right microphone is selected in Windows Settings, System, Sound, Input.");
    }

    private static void Log(string line)
    {
        try { File.AppendAllText(AppPaths.FileIn("speech.log"), $"{DateTime.Now:s}  {line}{Environment.NewLine}"); }
        catch (IOException) { }
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
        if (e.Result.Confidence < 0.6f)
        {
            Notice?.Invoke("I didn't catch that clearly. Try again, a little closer to the microphone.");
            return;
        }
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
