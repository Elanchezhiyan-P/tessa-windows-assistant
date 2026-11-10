using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// Start-up programs, search results and the junk-file summary shown as cards you can use, instead of text you have to read.
public partial class MainWindow
{
    /// <summary>
    /// Adds a card to the chat. The card is live only for this session, so a plain-text version is saved to the history,
    /// which is what shows after a restart and in an export.
    /// </summary>
    private void ShowCard(FrameworkElement card, string historyText)
    {
        _chat.Append(new ChatEntry("assistant", historyText, DateTime.Now));
        Messages.Children.Add(card);
        _historyOpen = true;
        _settingsOpen = false;
        RefreshLayout();
    }

    // ---- /startup ------------------------------------------------------------------------------------------

    private bool ShowStartupCard()
    {
        var items = new StartupApps().List();
        if (items.Count == 0) return false;

        AddMessage("user", "/startup");
        ShowCard(ResultCards.StartupList(items, ToggleStartup), StartupApps.Describe(items));
        return true;
    }

    // A click on a switch is the user's own decision (like the switches in Settings), so it needs no extra approval; it is logged.
    private (bool Enabled, string Message) ToggleStartup(StartupItem item, bool enabled)
    {
        var apps = new StartupApps();
        var message = apps.SetEnabled(item.Name, enabled, only: item.Source);
        _log.Write("set_startup_app", new JsonObject { ["name"] = item.Name, ["enabled"] = enabled }, "approved", message);

        var now = apps.List().FirstOrDefault(i => i.Source == item.Source && i.Name == item.Name);
        return (now?.Enabled ?? !enabled, message);
    }

    // ---- /find -----------------------------------------------------------------------------------------------

    /// <summary>Shows search results as a card. False when there is nothing to show as one (no index, no words, no matches).</summary>
    private bool ShowFindCard(string args)
    {
        if (args.Length == 0 || FileIndex.Stats().Folders == 0) return false;
        var (query, after, before) = FileIndex.ParseFindArgs(args);
        if (query.Length == 0) return false;

        var hits = FileIndex.Search(query, after, before, 10);
        if (hits.Count == 0) return false;

        AddMessage("user", "/find " + args);
        ShowCard(ResultCards.SearchResults(query, hits, OpenDocument, RevealInExplorer, DocumentText.IsSafeToOpen), FileIndex.Describe(hits, query));
        return true;
    }

    private void OpenDocument(string path)
    {
        // The card only offers this for documents; checked again here so nothing else can ever run a script this way.
        if (!DocumentText.IsSafeToOpen(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            AddMessage("error", $"I couldn't open {System.IO.Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private void RevealInExplorer(string path)
    {
        try
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            info.ArgumentList.Add("/select,");
            info.ArgumentList.Add(path);
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AddMessage("error", $"I couldn't show the folder: {ex.Message}");
        }
    }

    // ---- /cleanup scan ---------------------------------------------------------------------------------------

    /// <summary>Scans, then shows the result with a Clean up button. Returns text instead when there is nothing to clean.</summary>
    private async Task<string> ShowCleanupCardAsync()
    {
        var found = await Task.Run(() => TempCleanup.Scan());
        if (found.Sum(f => f.Bytes) == 0) return TempCleanup.Describe(found);

        AddMessage("user", "/cleanup scan");
        ShowCard(ResultCards.Cleanup(found, CleanNow), TempCleanup.Describe(found));
        return "";
    }

    private async Task<string> CleanNow()
    {
        var result = await Task.Run(() => TempCleanup.Clean());
        var message = TempCleanup.Describe(result);
        _log.Write("clean_temp_files", new JsonObject(), "approved", message);
        return message;
    }
}
