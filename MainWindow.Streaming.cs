using System.Windows;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// Replies that appear as they are written, and a Stop button for a reply that is taking too long or going the wrong way.
public partial class MainWindow
{
    private CancellationTokenSource? _chatStop;
    private bool _liveActive;
    private FrameworkElement? _liveBubble;
    private string _liveText = "";
    private bool _liveDirty;
    private DispatcherTimer? _liveTimer;

    /// <summary>Shows the Stop button in place of Send, and gets ready to show the answer as it arrives.</summary>
    private void BeginLiveReply()
    {
        _liveActive = true;
        _liveText = "";
        _liveDirty = false;
        StopButton.Visibility = Visibility.Visible;
        SendButton.Visibility = Visibility.Collapsed;

        // Redrawing the formatted text for every word would be wasteful: it is refreshed about ten times a second.
        _liveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, (_, _) => FlushLiveReply(), Dispatcher);
        _liveTimer.Start();
    }

    /// <summary>The model reported the answer so far. An empty text means a new round began (a tool ran), so the old text goes.</summary>
    private void OnLiveReply(string text)
    {
        if (!_liveActive) return; // a late report after the reply was finished

        _liveText = text;
        if (text.Length == 0)
        {
            RemoveLiveBubble();
            if (_typing is not null && !Messages.Children.Contains(_typing)) Messages.Children.Add(_typing);
            return;
        }

        if (_liveBubble is null)
        {
            if (_typing is not null) Messages.Children.Remove(_typing); // the dots give way to the text
            _liveBubble = LiveBubble(text);
            Messages.Children.Add(_liveBubble);
            _historyOpen = true;
            _settingsOpen = false;
            RefreshLayout();
        }
        else _liveDirty = true;
    }

    private static FrameworkElement LiveBubble(string text) =>
        ChatBubbles.Create(new ChatEntry("assistant", text, DateTime.Now), animate: false);

    private void FlushLiveReply()
    {
        if (!_liveDirty || _liveBubble is null) return;
        _liveDirty = false;

        var fresh = LiveBubble(_liveText);
        var index = Messages.Children.IndexOf(_liveBubble);
        if (index < 0) return;
        Messages.Children[index] = fresh;
        _liveBubble = fresh;
        Scroll.ScrollToBottom();
    }

    private void RemoveLiveBubble()
    {
        if (_liveBubble is not null) Messages.Children.Remove(_liveBubble);
        _liveBubble = null;
        _liveDirty = false;
    }

    /// <summary>Takes the live text away (the finished reply replaces it) and puts Send back. Returns whether there was live text.</summary>
    private bool EndLiveReply()
    {
        var had = _liveBubble is not null;
        _liveActive = false;
        _liveTimer?.Stop();
        RemoveLiveBubble();
        StopButton.Visibility = Visibility.Collapsed;
        SendButton.Visibility = Visibility.Visible;
        return had;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try { _chatStop?.Cancel(); }
        catch (ObjectDisposedException) { /* it just finished */ }
        ResolveConfirm(false); // a question waiting for Allow or Deny must not keep the reply alive
        Status.Text = "Stopping…";
    }
}
