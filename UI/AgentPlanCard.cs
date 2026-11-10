using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>
/// The plan shown in the chat while an agent task runs: the steps with their progress, and the buttons to run, cancel or stop it.
/// Before it starts the user can reword, move, remove or add steps, save the plan, and then choose Run plan or Cancel; while it runs
/// there is a Stop button.
/// </summary>
internal sealed class AgentPlanCard
{
    private readonly List<AgentStep> _steps;
    private readonly StackPanel _stepList = new();
    private readonly TextBlock _status = new() { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _run, _cancel, _stop, _save, _add;
    private readonly TaskCompletionSource<bool> _decision = new();
    private readonly Func<string, object> _res;
    private AgentStep? _editing;
    private bool _editable = true;

    public FrameworkElement Root { get; }

    /// <summary>The steps as they stand now (after any edits). This is the list the plan runs.</summary>
    public IReadOnlyList<AgentStep> Steps => _steps;

    /// <summary>Raised when the user presses Stop while the plan runs.</summary>
    public event Action? StopRequested;

    /// <summary>Called by Save plan with the steps' text; returns what to tell the user. Without it there is no Save button.</summary>
    public Func<IReadOnlyList<string>, string>? SavePlan { get; set; }

    /// <param name="resources">Looks up the app's colours and button styles by name. Null uses the running app's; tests pass their own.</param>
    public AgentPlanCard(string goal, List<AgentStep> steps, Func<string, object>? resources = null)
    {
        _steps = steps;
        _res = resources ?? (key => Application.Current.FindResource(key));
        var fg = (Brush)_res("Fg");
        _status.Foreground = (Brush)_res("Muted");

        _run = new Button { Content = "Run plan", Style = (Style)_res("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        _cancel = new Button { Content = "Cancel", Style = (Style)_res("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        _save = new Button { Content = "Save plan", Style = (Style)_res("GhostButton"), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        _add = new Button { Content = "＋ Add a step", Style = (Style)_res("GhostButton") };
        _stop = new Button { Content = "Stop after this step", Style = (Style)_res("GhostButton"), Visibility = Visibility.Collapsed };

        _run.Click += (_, _) =>
        {
            if (_steps.Count == 0) { _status.Text = "A plan needs at least one step."; return; }
            FinishEditing(commit: true);
            ShowRunning();
            _decision.TrySetResult(true);
        };
        _cancel.Click += (_, _) => _decision.TrySetResult(false);
        _save.Click += (_, _) =>
        {
            FinishEditing(commit: true);
            if (SavePlan is not null) _status.Text = SavePlan(_steps.Select(s => s.Text).ToList());
        };
        _add.Click += (_, _) =>
        {
            if (_steps.Count >= AgentPlanner.MaxSteps) return;
            FinishEditing(commit: true);
            var step = new AgentStep(_steps.Count + 1, "");
            _steps.Add(step);
            _editing = step;
            Refresh();
        };
        _stop.Click += (_, _) =>
        {
            _stop.IsEnabled = false;
            _status.Text = "Stopping after the current step…";
            StopRequested?.Invoke();
        };
        foreach (var button in new[] { _run, _cancel, _save, _add, _stop }) _buttons.Children.Add(button);

        _status.Text = "I'll do these in order. You can reword, move or remove a step first. Anything risky still asks you first, and everything is logged.";
        var title = new TextBlock
        {
            Text = $"Plan: {goal}",
            Foreground = fg,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Root = new Border
        {
            Background = (Brush)_res("Surface"),
            BorderBrush = (Brush)_res("Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 4, 0, 8),
            Child = new StackPanel { Children = { title, _stepList, _status, _buttons } }
        };
        Refresh();
    }

    /// <summary>Redraws the step list. Call after any step changes.</summary>
    public void Refresh()
    {
        _stepList.Children.Clear();
        _save.Visibility = _editable && SavePlan is not null ? Visibility.Visible : Visibility.Collapsed;
        _add.Visibility = _editable && _steps.Count < AgentPlanner.MaxSteps ? Visibility.Visible : Visibility.Collapsed;

        var fg = (Brush)_res("Fg");
        var muted = (Brush)_res("Muted");
        var accent = (Brush)_res("Accent");

        foreach (var step in _steps)
        {
            if (ReferenceEquals(step, _editing))
            {
                _stepList.Children.Add(EditorFor(step));
                continue;
            }

            var row = new TextBlock
            {
                Text = $"{AgentPlanner.Glyph(step.Status)}  {step.Number}. {step.Text}",
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Foreground = step.Status switch
                {
                    StepStatus.Running => accent,
                    StepStatus.Done => fg,
                    StepStatus.Failed => Brushes.IndianRed,
                    _ => muted
                },
                FontWeight = step.Status == StepStatus.Running ? FontWeights.SemiBold : FontWeights.Normal
            };
            _stepList.Children.Add(_editable ? WithControls(row, step) : row);
        }
    }

    // ---- Editing ------------------------------------------------------------------------------------

    private FrameworkElement WithControls(TextBlock row, AgentStep step)
    {
        var index = _steps.IndexOf(step);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(row);

        var up = Control("", "Move up", index > 0, () => Move(step, -1));
        var down = Control("", "Move down", index < _steps.Count - 1, () => Move(step, +1));
        var edit = Control("", "Reword this step", true, () => { FinishEditing(commit: true); _editing = step; Refresh(); });
        var remove = Control("", "Remove this step", _steps.Count > 1, () => Remove(step));
        var column = 1;
        foreach (var button in new[] { up, down, edit, remove })
        {
            Grid.SetColumn(button, column++);
            grid.Children.Add(button);
        }
        return grid;
    }

    private Button Control(string glyph, string tip, bool enabled, Action action)
    {
        var button = new Button
        {
            Content = glyph, ToolTip = tip, Style = (Style)_res("IconButton"), Width = 24, Height = 24, FontSize = 10.5,
            IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Top
        };
        button.Click += (_, _) => action();
        return button;
    }

    private FrameworkElement EditorFor(AgentStep step)
    {
        var box = new TextBox { Text = step.Text, Style = (Style)_res("Field"), MaxLength = PlanStore.MaxStepLength, Margin = new Thickness(0, 0, 0, 4), AcceptsReturn = false };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Commit(step, box.Text); }
            else if (e.Key == Key.Escape) { e.Handled = true; Commit(step, null); }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        box.LostKeyboardFocus += (_, _) => { if (ReferenceEquals(_editing, step)) Commit(step, box.Text); };
        return box;
    }

    // Applies (or, with null text, drops) the edit. A step left empty is removed.
    private void Commit(AgentStep step, string? text)
    {
        if (!ReferenceEquals(_editing, step)) return;
        _editing = null;
        if (text is not null) step.Text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (step.Text.Length == 0 && _steps.Count > 1) _steps.Remove(step);
        else if (step.Text.Length == 0) step.Text = "Do the task";
        Renumber();
        Refresh();
    }

    private void FinishEditing(bool commit)
    {
        if (_editing is not { } step) return;
        Commit(step, commit && Root is not null ? FindEditor()?.Text : null);
    }

    private TextBox? FindEditor() => _stepList.Children.OfType<TextBox>().FirstOrDefault();

    private void Move(AgentStep step, int by)
    {
        FinishEditing(commit: true);
        var from = _steps.IndexOf(step);
        var to = from + by;
        if (from < 0 || to < 0 || to >= _steps.Count) return;
        _steps.RemoveAt(from);
        _steps.Insert(to, step);
        Renumber();
        Refresh();
    }

    private void Remove(AgentStep step)
    {
        if (_steps.Count <= 1) return;
        FinishEditing(commit: true);
        _steps.Remove(step);
        Renumber();
        Refresh();
    }

    private void Renumber()
    {
        for (var i = 0; i < _steps.Count; i++) _steps[i].Number = i + 1;
    }

    // ---- Decision and progress ----------------------------------------------------------------------

    /// <summary>Waits for Run plan (true) or Cancel (false).</summary>
    public Task<bool> WaitForDecisionAsync() => _decision.Task;

    /// <summary>Answers "Cancel" for the user, if they haven't decided yet (for example because the chat was cleared).</summary>
    public void Cancel() => _decision.TrySetResult(false);

    private void ShowRunning()
    {
        _editable = false;
        _run.Visibility = _cancel.Visibility = _save.Visibility = _add.Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Visible;
        _status.Text = "Running… Press Stop to end it after the current step.";
        Refresh();
    }

    /// <summary>The plan is over: hide the buttons and say how it ended.</summary>
    public void ShowFinished(string message)
    {
        _editable = false;
        _buttons.Visibility = Visibility.Collapsed;
        _status.Text = message;
        Refresh();
    }
}
