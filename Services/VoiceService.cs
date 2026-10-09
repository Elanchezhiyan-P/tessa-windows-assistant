using System.Speech.Recognition;

namespace WinCompanion.Services;

/// <summary>
/// Windows speech recognition: an optional always-on wake phrase (for example "Hey Tessa"),
/// plus a dictation window that opens after the wake phrase or on demand (push-to-talk hotkey).
/// Events fire on a background thread.
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
            _timeout = new System.Threading.Timer(_ => EndListening(), null, DictationTimeout, Timeout.InfiniteTimeSpan);
            Apply();
        }
        ListeningStarted?.Invoke();
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
        var shouldRun = _wakeEnabled || _listening;
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
        if (_dictationGrammar is not null) _dictationGrammar.Enabled = _listening;
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
