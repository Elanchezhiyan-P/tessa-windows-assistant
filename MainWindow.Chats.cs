using System.Windows;
using System.Windows.Media.Animation;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// Several named chats you can come back to, and messages you pin so they are easy to find again.
public partial class MainWindow
{
    private ChatListView _chatList = null!;
    private bool _needSeed = true; // the model's memory is built from the open chat the first time it is needed

    // A reply being written belongs to the chat it was asked in, so chats are not switched under it.
    private bool ReplyInProgress()
    {
        if (_chatStop is null) return false;
        Status.Text = "Wait for the reply to finish, or press Stop, before changing chats.";
        return true;
    }

    private void InitChats()
    {
        _chatList = new ChatListView(key => Application.Current.FindResource(key));
        ChatsPopup.Child = _chatList.Element;
        ChatBubbles.TogglePin = id => _chat.TogglePin(id);

        _chatList.NewChat += () => { ChatsPopup.IsOpen = false; StartNewChat(); };
        _chatList.OpenChat += id => { ChatsPopup.IsOpen = false; SwitchChat(id); };
        _chatList.DeleteChat += id =>
        {
            var wasOpen = id == _chat.ActiveId;
            if (wasOpen && ReplyInProgress()) return;
            _chat.Delete(id);
            if (wasOpen) ReloadChat(resetModel: true);
            RefreshChatList();
        };
        _chatList.RenameChat += (id, title) =>
        {
            _chat.Rename(id, title);
            RefreshChatTitle();
            RefreshChatList();
        };
        _chatList.OpenPin += pin => { ChatsPopup.IsOpen = false; JumpToMessage(pin); };
        _chatList.Unpin += pin =>
        {
            _chat.TogglePin(pin.Entry.Id);
            ReloadChat(resetModel: false);
            RefreshChatList(pins: true);
        };
    }

    private void Chats_Click(object sender, RoutedEventArgs e)
    {
        if (ChatsPopup.IsOpen) { ChatsPopup.IsOpen = false; return; }
        OpenChatList(pins: false);
    }

    private void OpenChatList(bool pins)
    {
        ShowAndFocus();
        _chatList.Show(_chat.Conversations(), _chat.Pins(), _chat.ActiveId, pins);
        ChatsPopup.IsOpen = true;
    }

    private void RefreshChatList(bool pins = false)
    {
        if (ChatsPopup.IsOpen) _chatList.Show(_chat.Conversations(), _chat.Pins(), _chat.ActiveId, pins);
    }

    // ---- Open, new, clear ---------------------------------------------------------------------------

    /// <summary>Starts a fresh chat. The one you were in stays in your list.</summary>
    private void StartNewChat()
    {
        if (ReplyInProgress()) return;
        StopAgent();
        ClearSelection();
        _chat.StartNew();
        ReloadChat(resetModel: true);
        Input.Focus();
    }

    /// <summary>Empties the open chat and its place in the model's memory, and keeps the chat itself.</summary>
    private void ClearChat()
    {
        if (ReplyInProgress()) return;
        StopAgent();
        ClearSelection();
        _chat.Clear();
        ReloadChat(resetModel: true);
        Input.Focus();
    }

    private async Task<string> DeleteAllChatsAsync()
    {
        if (ReplyInProgress()) return "";
        if (!await ConfirmAsync($"Delete all {_chat.Conversations().Count} chat(s) and every pinned message?\nThis can't be undone.")) return "Nothing was deleted.";
        StopAgent();
        ClearSelection();
        _chat.DeleteAll();
        ReloadChat(resetModel: true);
        return "Deleted every chat.";
    }

    private void SwitchChat(long id)
    {
        if (id == _chat.ActiveId || ReplyInProgress()) return;
        StopAgent();
        ClearSelection();
        if (!_chat.Switch(id)) return;
        ReloadChat(resetModel: true);
        Input.Focus();
    }

    // Shows the open chat again, and (after a switch) points the model's memory at it.
    private void ReloadChat(bool resetModel)
    {
        if (resetModel)
        {
            _llm?.ClearHistory();
            _needSeed = true;
        }
        Messages.Children.Clear();
        _olderButton = null;
        foreach (var entry in _chat.Entries) Messages.Children.Add(ChatBubbles.Create(entry, animate: false));
        UpdateOlderButton();
        RefreshChatTitle();
        _historyOpen = _chat.Entries.Count > 0 || _historyOpen;
        RefreshLayout();
    }

    private void RefreshChatTitle()
    {
        var title = _chat.ActiveTitle;
        HeaderChat.Text = title == Database.DefaultChatTitle ? "" : "·  " + title;
        HeaderChat.ToolTip = title;
    }

    // The model forgets between chats, so a chat that is opened again is read back to it (the last few exchanges).
    private void SeedIfNeeded(ILlm client)
    {
        if (!_needSeed) return;
        _needSeed = false;
        var recent = _chat.Entries
            .Where(e => e.Role is "user" or "assistant" && e.Text.Length > 0)
            .TakeLast(16)
            .Select(e => (e.Role, Text: e.Text.Length > 4000 ? e.Text[..4000] : e.Text))
            .ToList();
        if (recent.Count > 0) client.SeedHistory(recent);
    }

    // ---- Commands -----------------------------------------------------------------------------------

    private string OpenChatByName(string args)
    {
        var chats = _chat.Conversations();
        Conversation? target = null;
        if (int.TryParse(args.Trim(), out var number) && number >= 1 && number <= chats.Count) target = chats[number - 1];
        else target = chats.FirstOrDefault(c => c.Title.Equals(args.Trim(), StringComparison.OrdinalIgnoreCase))
                      ?? chats.FirstOrDefault(c => c.Title.Contains(args.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return chats.Count == 0 ? "You have no chats yet." : "I can't find that chat. Type /chats to see them, then /chat 2 or /chat part of the name.";
        if (target.Id == _chat.ActiveId) return $"You are already in \"{target.Title}\".";
        SwitchChat(target.Id);
        return "";
    }

    private string RenameActiveChat(string args)
    {
        if (!_chat.Rename(_chat.ActiveId, args)) return "Type the name, for example /rename Holiday plans.";
        RefreshChatTitle();
        return $"This chat is now called \"{_chat.ActiveTitle}\".";
    }

    private string PinLastReply()
    {
        var last = _chat.Entries.LastOrDefault(e => e.Role == "assistant" && e.Id > 0);
        if (last is null) return "There is no reply to pin yet.";
        var pinned = _chat.TogglePin(last.Id);
        ReloadChat(resetModel: false);
        return pinned ? "Pinned my last reply. /pins lists everything you pinned." : "Unpinned my last reply.";
    }

    // Opens the chat a pinned message is in, scrolls to it and lets it flash once.
    private void JumpToMessage(PinnedMessage pin)
    {
        if (pin.ConversationId != _chat.ActiveId) SwitchChat(pin.ConversationId);
        for (var i = 0; i < 20 && _chat.Entries.All(e => e.Id != pin.Entry.Id) && _chat.HasOlder; i++) _chat.LoadOlder(200);
        ReloadChat(resetModel: false);

        var element = Messages.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag is long id && id == pin.Entry.Id);
        if (element is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            element.BringIntoView();
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(700)));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
