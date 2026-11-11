using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinCompanion.Services;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

// "/backup" and "/restore", and the matching buttons in Settings, PC & files. A backup can be locked with a password.
public partial class MainWindow
{
    private static string BackupFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinCompanion", "Backups");

    private const string LockedExtension = ".tessabak";

    // The password is asked for in a box, never typed into the chat (where it would be saved in the history).
    private string? AskNewBackupPassword() => PasswordPrompt.Ask(this, "Lock this backup",
        "Choose a password for the backup. It holds what I remember about you and your notes, so a password keeps it private if the file is ever lost.\n\n" +
        "I can't recover a forgotten password. Leave it empty to save a backup that anyone with the file can read.",
        confirm: true, allowEmpty: true, validate: BackupCrypto.CheckPassword);

    private string BackupNow(string? path, string password)
    {
        try
        {
            var locked = password.Length > 0;
            path ??= Path.Combine(BackupFolder, $"tessa-backup-{DateTime.Now:yyyyMMdd-HHmm}{(locked ? LockedExtension : ".zip")}");
            Backup.Create(path, _appSettings, _memory, _routines, _reminders, NotesTools.NotesPath, _plans, password);
            return $"Backup saved to {path}\nIt holds your settings, what I remember, routines, plans, reminders and notes, and no API key or chat history. " +
                   (locked ? "It is locked with your password: keep the password safe, I can't recover it. " : "It is NOT locked, so keep the file private. ") +
                   "On the new PC, put it anywhere and type /restore.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "I couldn't write the backup: " + ex.Message;
        }
    }

    private string BackupCommand(string? path)
    {
        var password = AskNewBackupPassword();
        return password is null ? "Backup cancelled." : BackupNow(path, password);
    }

    private async Task<string> RestoreAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a backup to restore",
                Filter = "Backups (*.tessabak;*.zip)|*.tessabak;*.zip",
                InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog(this) != true) return "Restore cancelled.";
            path = dialog.FileName;
        }
        path = path.Trim().Trim('"');
        if (!File.Exists(path)) return $"I can't find {path}.";

        // A locked backup needs its password (three tries).
        string? password = null;
        BackupSummary? summary = null;
        string error = "";
        if (Backup.IsLocked(path))
        {
            string? why = null;
            for (var attempt = 0; attempt < 3 && summary is null; attempt++)
            {
                password = PasswordPrompt.Ask(this, "Locked backup", "This backup is locked. Type its password.", confirm: false, allowEmpty: false, error: why);
                if (password is null) return "Restore cancelled.";
                summary = Backup.Inspect(path, out error, password);
                why = error;
            }
            if (summary is null) return error + " Nothing was changed.";
        }
        else
        {
            summary = Backup.Inspect(path, out error);
            if (summary is null) return error;
        }

        var allowed = await ConfirmAsync(
            $"Restore this backup?\n{summary.Describe()}\n" +
            "It adds to what you have; nothing is deleted. Settings that switch off a safety question or run commands unprompted are never restored.");
        if (!allowed) return "Restore cancelled. Nothing was changed.";

        try
        {
            var report = Backup.Restore(path, _appSettings, _memory, _routines, _reminders, NotesTools.NotesPath,
                name => _tools.Find(name) is not null, _plans, password);
            _appSettings.Save();
            ApplyTheme(_appSettings.Theme, _appSettings.AccentColor, _appSettings.ColorMode);
            ApplySize(_appSettings.InterfaceSize);
            ApplyIdentity();
            return report.Describe() + "\nAPI keys are not in a backup: add them again in Settings, AI model.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Restore failed", ex);
            return "I couldn't restore that backup: " + ex.Message;
        }
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var password = AskNewBackupPassword();
        if (password is null) return;
        var locked = password.Length > 0;
        var dialog = new SaveFileDialog
        {
            Title = "Save a backup",
            Filter = locked ? "Locked backup (*.tessabak)|*.tessabak" : "Backup (*.zip)|*.zip",
            FileName = $"tessa-backup-{DateTime.Now:yyyyMMdd}{(locked ? LockedExtension : ".zip")}"
        };
        if (dialog.ShowDialog(this) != true) return;
        BackupStatus.Text = BackupNow(dialog.FileName, password);
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e) => BackupStatus.Text = await RestoreAsync(null);
}
