using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using WinCompanion.Services;
using WinCompanion.Tools;

namespace WinCompanion;

// PC care (start-up programs, junk files, "why is it slow?") and searching the user's documents: the Settings cards and the commands.
public partial class MainWindow
{
    private sealed class Win32Owner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    /// <summary>Fills the start-up list and the indexed folders. Called whenever Settings opens.</summary>
    private void LoadCare()
    {
        RefreshStartupList();
        RefreshIndexList();
        CleanupRunButton.IsEnabled = false;
        CleanupRunButton.Content = "Clean up";
        _cleanupArmed = false;
        _indexClearArmed = false;
        IndexClearButton.Content = "Forget all";
    }

    // ---- Slow? -------------------------------------------------------------------------------------------

    private async void SlowCheck_Click(object sender, RoutedEventArgs e)
    {
        SlowButton.IsEnabled = false;
        SlowText.Text = "Checking for a few seconds…";
        try { SlowText.Text = await PcCareTools.DiagnoseAsync(); }
        finally { SlowButton.IsEnabled = true; }
    }

    // ---- Start-up programs -------------------------------------------------------------------------------

    private void RefreshStartupList()
    {
        StartupList.Children.Clear();
        IReadOnlyList<StartupItem> items;
        try { items = new StartupApps().List(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            StartupList.Children.Add(Muted("I couldn't read the start-up list."));
            return;
        }

        if (items.Count == 0)
        {
            StartupList.Children.Add(Muted("No programs are set to start with Windows."));
            return;
        }

        foreach (var item in items)
        {
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Switch"),
                Content = item.Label + item.Tag,
                IsChecked = item.Enabled,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = item.Command
            };
            var captured = item;
            toggle.Click += (_, _) =>
            {
                var enabled = toggle.IsChecked == true;
                var message = new StartupApps().SetEnabled(captured.Name, enabled, only: captured.Source);
                _log.Write("set_startup_app", new JsonObject { ["name"] = captured.Name, ["enabled"] = enabled }, "approved", message);
                SettingsNote.Text = message;
                RefreshStartupList();
            };
            StartupList.Children.Add(toggle);
        }
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    // ---- Junk files ----------------------------------------------------------------------------------------

    private bool _cleanupArmed;

    private async void CleanupScan_Click(object sender, RoutedEventArgs e)
    {
        CleanupText.Text = "Looking…";
        _cleanupArmed = false;
        var found = await Task.Run(() => TempCleanup.Scan());
        CleanupText.Text = TempCleanup.Describe(found);

        var total = found.Sum(f => f.Bytes);
        CleanupRunButton.IsEnabled = total > 0;
        CleanupRunButton.Content = total > 0 ? $"Clean up {SystemInfo.FormatBytes(total)}" : "Clean up";
    }

    // Deleting can't be undone, so it takes a second click.
    private async void CleanupRun_Click(object sender, RoutedEventArgs e)
    {
        if (!_cleanupArmed)
        {
            _cleanupArmed = true;
            CleanupRunButton.Content = "Click again to delete them for good";
            return;
        }

        _cleanupArmed = false;
        CleanupRunButton.IsEnabled = false;
        CleanupText.Text = "Cleaning…";
        var result = await Task.Run(() => TempCleanup.Clean());
        var message = TempCleanup.Describe(result);
        _log.Write("clean_temp_files", new JsonObject(), "approved", message);
        CleanupText.Text = message;
        CleanupRunButton.Content = "Clean up";
    }

    // ---- Searching documents -----------------------------------------------------------------------------

    private bool _indexing, _indexClearArmed;
    private CancellationTokenSource? _indexCancel;

    private void RefreshIndexList()
    {
        IndexList.Children.Clear();
        IReadOnlyList<string> folders;
        IndexStats stats;
        try
        {
            folders = FileIndex.Folders();
            stats = FileIndex.Stats();
        }
        catch (SqliteException)
        {
            IndexStatus.Text = "The index isn't available right now.";
            return;
        }

        foreach (var folder in folders)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = folder,
                Foreground = (System.Windows.Media.Brush)FindResource("Fg"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var remove = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = "",
                Width = 26,
                Height = 26,
                FontSize = 11,
                ToolTip = "Stop indexing this folder and forget its documents",
                Margin = new Thickness(6, 0, 0, 0)
            };
            var path = folder;
            remove.Click += (_, _) =>
            {
                SettingsNote.Text = FileIndex.RemoveFolder(path);
                RefreshIndexList();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(text);
            row.Children.Add(remove);
            IndexList.Children.Add(row);
        }

        IndexStatus.Text = _indexing ? "Reading your documents…" : FileIndex.DescribeStats(stats);
    }

    private void IndexAdd_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder with your documents",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;

        SettingsNote.Text = FileIndex.AddFolder(dialog.SelectedPath);
        RefreshIndexList();
        StartIndexing(announce: true);
    }

    private void IndexUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (FileIndex.Folders().Count == 0) { SettingsNote.Text = "Add a folder first."; return; }
        StartIndexing(announce: true);
        SettingsNote.Text = "Reading your documents in the background…";
    }

    private void IndexClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_indexClearArmed)
        {
            _indexClearArmed = true;
            IndexClearButton.Content = "Click again to forget all";
            return;
        }
        _indexClearArmed = false;
        IndexClearButton.Content = "Forget all";
        _indexCancel?.Cancel();
        FileIndex.Clear();
        SettingsNote.Text = "Forgot every indexed document and folder.";
        RefreshIndexList();
    }

    /// <summary>Reads new and changed documents in the background at low priority, and tells the user when it found something.</summary>
    private async void StartIndexing(bool announce)
    {
        if (_indexing) return;
        _indexing = true;
        _indexCancel = new CancellationTokenSource();
        var token = _indexCancel.Token;
        var progress = new Progress<string>(message => Status.Text = message);
        try
        {
            var summary = await Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; // never slow down what the user is doing
                return FileIndex.Update(progress, token);
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            if (!token.IsCancellationRequested && (announce || summary.Changed)) AddMessage("assistant", FileIndex.Describe(summary));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (ex is not OperationCanceledException) AddMessage("error", $"Indexing stopped: {ex.Message}");
        }
        finally
        {
            _indexing = false;
            if (!_agentRunning) Status.Text = "";
            if (_settingsOpen) RefreshIndexList();
        }
    }

    /// <summary>A while after start, brings the index up to date quietly (only mentioning it if something changed).</summary>
    private void ScheduleIndexRefresh() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(90));
            try { if (FileIndex.Folders().Count > 0) StartIndexing(announce: false); }
            catch (SqliteException) { }
        });

    // ---- Commands ------------------------------------------------------------------------------------------

    private async Task<string> StartupCommandAsync(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return StartupApps.Describe(new StartupApps().List());

        var enable = parts[0].Equals("on", StringComparison.OrdinalIgnoreCase);
        if ((!enable && !parts[0].Equals("off", StringComparison.OrdinalIgnoreCase)) || parts.Length < 2)
            return "Use /startup to list, /startup off <name> to stop one, or /startup on <name> to allow it again.";

        // Through the guarded executor, so it asks first and is logged like the same request made in chat.
        if (_ctx.Executor is null) return "That isn't ready yet.";
        return await _ctx.Executor.RunAsync("set_startup_app", new JsonObject { ["name"] = parts[1].Trim('"'), ["enabled"] = enable });
    }

    private async Task<string> CleanupCommandAsync(string args)
    {
        if (args.Equals("scan", StringComparison.OrdinalIgnoreCase) || args.Equals("check", StringComparison.OrdinalIgnoreCase))
            return TempCleanup.Describe(await Task.Run(() => TempCleanup.Scan()));

        if (_ctx.Executor is null) return "That isn't ready yet.";
        return await _ctx.Executor.RunAsync("clean_temp_files", new JsonObject());
    }

    private string FindCommand(string args)
    {
        if (args.Length == 0) return "Type what to look for, for example /find invoice electricity, or /find contract after:2026-03.";
        var (query, after, before) = FileIndex.ParseFindArgs(args);
        if (FileIndex.Stats().Folders == 0) return FileIndex.DescribeStats(new IndexStats(0, 0, 0, 0));
        if (query.Length == 0) return "Add some words to look for.";
        return FileIndex.Describe(FileIndex.Search(query, after, before, 10), query);
    }

    private string IndexCommand(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var rest = parts.Length > 1 ? parts[1].Trim('"') : "";

        switch (verb)
        {
            case "":
                var folders = FileIndex.Folders();
                return FileIndex.DescribeStats(FileIndex.Stats()) + (folders.Count > 0 ? "\n" + string.Join('\n', folders.Select(f => $"• {f}")) : "") +
                       "\nUse /index add <folder>, /index remove <folder>, /index now or /index clear.";
            case "add":
                if (rest.Length == 0) return "Say which folder, for example /index add Documents.";
                var added = FileIndex.AddFolder(BuiltInTools.ResolveFolder(rest));
                if (added.StartsWith("Added", StringComparison.Ordinal)) StartIndexing(announce: true);
                return added + (added.StartsWith("Added", StringComparison.Ordinal) ? " I'm reading its documents in the background." : "");
            case "remove":
                return rest.Length == 0 ? "Say which folder to remove." : FileIndex.RemoveFolder(BuiltInTools.ResolveFolder(rest));
            case "now":
            case "update":
                if (FileIndex.Folders().Count == 0) return "Add a folder first: /index add <folder>.";
                StartIndexing(announce: true);
                return "Updating the index in the background.";
            case "stop":
                _indexCancel?.Cancel();
                return "Stopped.";
            case "clear":
                _indexCancel?.Cancel();
                FileIndex.Clear();
                return "Forgot every indexed document and folder.";
            default:
                return "Use /index, /index add <folder>, /index remove <folder>, /index now or /index clear.";
        }
    }
}
