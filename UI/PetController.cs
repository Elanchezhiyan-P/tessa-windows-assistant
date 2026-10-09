using WinCompanion.Services;
using WinCompanion.Tools;

namespace WinCompanion.UI;

/// <summary>Brings the chosen character on screen for each reminder, one at a time.</summary>
internal sealed class PetController
{
    private sealed record Request(PetDefinition Pet, string Message, Action? Snooze, Action? Done);

    private const int NotificationBusy = 2, NotificationFullScreenGame = 3, NotificationPresentation = 4;

    private readonly AppSettings _settings;
    private readonly Queue<Request> _queue = new();
    private PetWindow? _active;

    public PetController(AppSettings settings) => _settings = settings;

    /// <summary>The selected character, or null when the user chose "none" or the pack is missing.</summary>
    private PetDefinition? Selected =>
        _settings.Pet.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : PetCatalog.Find(_settings.Pet) ?? PetCatalog.All().FirstOrDefault();

    /// <summary>
    /// Shows the character with the reminder. Returns false if it can't (disabled, or a full-screen app is in the
    /// way), in which case the caller falls back to a normal notification.
    /// </summary>
    public bool Deliver(string message, Action snooze, Action? done)
    {
        if (Selected is not { } pet || IsFullScreenAppRunning()) return false;
        _queue.Enqueue(new Request(pet, message, snooze, done));
        Pump();
        return true;
    }

    /// <summary>For the Settings "Preview" button: ignores the saved choice and the full-screen check.</summary>
    public void Preview(string id)
    {
        if (PetCatalog.Find(id) is not { } pet) return;
        _queue.Enqueue(new Request(pet, "Hi! I'll bring you your reminders.", Snooze: null, Done: null));
        Pump();
    }

    public static bool IsFullScreenAppRunning() =>
        NativeMethods.SHQueryUserNotificationState(out var state) == 0 &&
        state is NotificationBusy or NotificationFullScreenGame or NotificationPresentation;

    private void Pump()
    {
        if (_active is not null || _queue.Count == 0) return;

        var request = _queue.Dequeue();
        var window = new PetWindow(request.Pet, request.Message, request.Snooze, request.Done);
        _active = window;
        window.Finished += () =>
        {
            _active = null;
            Pump(); // the next queued reminder, if any
        };
        window.Show();
    }
}
