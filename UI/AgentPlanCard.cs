using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>
/// The plan shown in the chat while an agent task runs: the steps with their progress, and the buttons to run, cancel or stop it.
/// Before it starts the user chooses Run plan or Cancel; while it runs there is a Stop button.
/// </summary>
internal sealed class AgentPlanCard
{
    private readonly IReadOnlyList<AgentStep> _steps;
    private readonly StackPanel _stepList = new();
    private readonly TextBlock _status = new() { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _stop;
    private readonly TaskCompletionSource<bool> _decision = new();
    private readonly Func<string, object> _res;

    public FrameworkElement Root { get; }

    /// <summary>Raised when the user presses Stop while the plan runs.</summary>
    public event Action? StopRequested;

    /// <param name="resources">Looks up the app's colours and button styles by name. Null uses the running app's; tests pass their own.</param>
    public AgentPlanCard(string goal, IReadOnlyList<AgentStep> steps, Func<string, object>? resources = null)
    {
        _steps = steps;
        _res = resources ?? (key => Application.Current.FindResource(key));
        var fg = (Brush)_res("Fg");
        var muted = (Brush)_res("Muted");
        _status.Foreground = muted;

        var run = new Button { Content = "Run plan", Style = (Style)_res("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Style = (Style)_res("GhostButton") };
        _stop = new Button { Content = "Stop after this step", Style = (Style)_res("GhostButton"), Visibility = Visibility.Collapsed };

        run.Click += (_, _) => { ShowRunning(); _decision.TrySetResult(true); };
        cancel.Click += (_, _) => _decision.TrySetResult(false);
        _stop.Click += (_, _) =>
        {
            _stop.IsEnabled = false;
            _status.Text = "Stopping after the current step…";
            StopRequested?.Invoke();
        };
        _buttons.Children.Add(run);
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(_stop);

        _status.Text = "I'll do these in order. Anything risky still asks you first, and everything is logged.";
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
        var fg = (Brush)_res("Fg");
        var muted = (Brush)_res("Muted");
        var accent = (Brush)_res("Accent");

        foreach (var step in _steps)
        {
            var row = new TextBlock
            {
                Text = $"{AgentPlanner.Glyph(step.Status)}  {step.Number}. {step.Text}",
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
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
            _stepList.Children.Add(row);
        }
    }

    /// <summary>Waits for Run plan (true) or Cancel (false).</summary>
    public Task<bool> WaitForDecisionAsync() => _decision.Task;

    /// <summary>Answers "Cancel" for the user, if they haven't decided yet (for example because the chat was cleared).</summary>
    public void Cancel() => _decision.TrySetResult(false);

    private void ShowRunning()
    {
        _buttons.Children[0].Visibility = Visibility.Collapsed;
        _buttons.Children[1].Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Visible;
        _status.Text = "Running… Press Stop to end it after the current step.";
    }

    /// <summary>The plan is over: hide the buttons and say how it ended.</summary>
    public void ShowFinished(string message)
    {
        _buttons.Visibility = Visibility.Collapsed;
        _status.Text = message;
        Refresh();
    }
}
