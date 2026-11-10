using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>
/// The list that drops down from the header: every chat (open, rename, delete) and every pinned message. It only builds the
/// view and reports what was clicked; the window does the work.
/// </summary>
internal sealed class ChatListView
{
    private readonly Func<string, object> _res;
    private readonly Border _host = new();
    private readonly StackPanel _body = new();

    private IReadOnlyList<Conversation> _chats = [];
    private IReadOnlyList<PinnedMessage> _pins = [];
    private long _activeId;
    private bool _showPins;

    public event Action<long>? OpenChat;
    public event Action<long>? DeleteChat;
    public event Action<long, string>? RenameChat;
    public event Action? NewChat;
    public event Action<PinnedMessage>? OpenPin;
    public event Action<PinnedMessage>? Unpin;

    public ChatListView(Func<string, object> res)
    {
        _res = res;
        _host.Width = 350;
        _host.Padding = new Thickness(10);
        _host.CornerRadius = new CornerRadius(14);
        _host.BorderThickness = new Thickness(1);
        _host.Background = Brush("PopupBackground");
        _host.BorderBrush = Brush("Stroke");
        _host.Child = _body;
    }

    public FrameworkElement Element => _host;

    public void Show(IReadOnlyList<Conversation> chats, IReadOnlyList<PinnedMessage> pins, long activeId, bool showPins = false)
    {
        _chats = chats;
        _pins = pins;
        _activeId = activeId;
        _showPins = showPins && pins.Count > 0;
        Rebuild();
    }

    private Brush Brush(string key) => (Brush)_res(key);

    private void Rebuild()
    {
        _body.Children.Clear();

        var tabs = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var segment = new Border
        {
            Background = Brush("SegmentBack"), CornerRadius = new CornerRadius(11), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tab($"Chats ({_chats.Count})", !_showPins, false), Tab($"Pinned ({_pins.Count})", _showPins, true) } }
        };
        tabs.Children.Add(segment);
        var add = new Button { Content = "＋ New chat", Style = (Style)_res("GhostButton"), Padding = new Thickness(10, 5, 10, 5) };
        add.Click += (_, _) => NewChat?.Invoke();
        Grid.SetColumn(add, 1);
        tabs.Children.Add(add);
        _body.Children.Add(tabs);

        var list = new StackPanel();
        if (_showPins)
        {
            foreach (var pin in _pins) list.Children.Add(PinRow(pin));
        }
        else if (_chats.Count == 0)
        {
            list.Children.Add(Note("No chats yet."));
        }
        else
        {
            foreach (var chat in _chats) list.Children.Add(ChatRow(chat));
        }
        if (!_showPins && _pins.Count == 0) { /* nothing pinned yet: the Pinned tab explains how */ }
        _body.Children.Add(new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        if (_pins.Count == 0 && _showPins == false)
            _body.Children.Add(Note("Pin a message with the pin button on it, and it will be listed here."));
    }

    private RadioButton Tab(string text, bool selected, bool pins)
    {
        var tab = new RadioButton { Content = text, GroupName = "chatlist", Style = (Style)_res("Segment"), IsChecked = selected };
        tab.Checked += (_, _) =>
        {
            if (_showPins == pins) return;
            _showPins = pins;
            Rebuild();
        };
        return tab;
    }

    private TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Brush("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 2)
    };

    // ---- Rows ---------------------------------------------------------------------------------------

    private FrameworkElement ChatRow(Conversation chat)
    {
        var active = chat.Id == _activeId;
        var title = new TextBlock
        {
            Text = chat.Title, Foreground = Brush("Fg"), FontSize = 13, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var sub = new TextBlock
        {
            Text = Describe(chat, DateTime.Now), Foreground = Brush("Muted"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0)
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };

        var rename = IconButton("", "Rename");
        var delete = IconButton("", "Delete this chat");
        var grid = new Grid { Margin = new Thickness(10, 7, 6, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(rename, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(rename);
        grid.Children.Add(delete);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand,
            Background = active ? Brush("AccentSoft") : Brush("Surface"),
            Tag = chat.Id
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            OpenChat?.Invoke(chat.Id);
        };

        rename.Click += (_, e) =>
        {
            e.Handled = true;
            var box = new TextBox { Text = chat.Title, Style = (Style)_res("Field"), MaxLength = 60, Margin = new Thickness(0, 0, 4, 0) };
            grid.Children.Remove(text);
            grid.Children.Insert(0, box);
            rename.Visibility = delete.Visibility = Visibility.Collapsed;
            box.KeyDown += (_, k) =>
            {
                if (k.Key == Key.Enter) { k.Handled = true; RenameChat?.Invoke(chat.Id, box.Text); }
                else if (k.Key == Key.Escape) { k.Handled = true; Rebuild(); }
            };
            box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        };

        var armed = false;
        delete.Click += (_, e) =>
        {
            e.Handled = true;
            if (!armed)
            {
                armed = true;
                delete.Content = "Delete?";
                delete.FontFamily = (FontFamily)_res("UiFont");
                delete.Foreground = Brush("ErrorText");
                delete.Width = double.NaN;
                delete.Padding = new Thickness(8, 0, 8, 0);
                return;
            }
            DeleteChat?.Invoke(chat.Id);
        };
        return row;
    }

    private FrameworkElement PinRow(PinnedMessage pin)
    {
        var who = pin.Entry.Role == "user" ? "You" : "Answer";
        var snippet = pin.Entry.Text.ReplaceLineEndings(" ").Trim();
        if (snippet.Length > 130) snippet = snippet[..130] + "…";

        var text = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = snippet, Foreground = Brush("Fg"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 54 },
                new TextBlock { Text = $"{who} · in {pin.ConversationTitle}", Foreground = Brush("Muted"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
        var unpin = IconButton("", "Unpin");
        unpin.Foreground = Brush("Accent");
        unpin.Click += (_, e) => { e.Handled = true; Unpin?.Invoke(pin); };

        var grid = new Grid { Margin = new Thickness(10, 7, 6, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(unpin, 1);
        grid.Children.Add(unpin);

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Background = Brush("Surface") };
        row.MouseLeftButtonUp += (_, e) => { if (!e.Handled) OpenPin?.Invoke(pin); };
        return row;
    }

    private Button IconButton(string glyph, string tip) => new()
    {
        Content = glyph, ToolTip = tip, Style = (Style)_res("IconButton"), Width = 28, Height = 28, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
    };

    // ---- Text ---------------------------------------------------------------------------------------

    /// <summary>"12 messages · today 14:05", with a pin count when there is one.</summary>
    internal static string Describe(Conversation chat, DateTime now)
    {
        var count = chat.Messages == 1 ? "1 message" : $"{chat.Messages} messages";
        var text = $"{count} · {When(chat.Updated, now)}";
        return chat.Pinned > 0 ? text + $" · {chat.Pinned} pinned" : text;
    }

    internal static string When(DateTime time, DateTime now)
    {
        if (time.Date == now.Date) return $"today {time:HH:mm}";
        if (time.Date == now.Date.AddDays(-1)) return $"yesterday {time:HH:mm}";
        return time.Year == now.Year ? time.ToString("d MMM") : time.ToString("d MMM yyyy");
    }
}
