using System.Windows;
using System.Windows.Controls;
using WinCompanion.Services;

namespace WinCompanion;

/// <summary>A three-step walkthrough of the shortcuts, the slash commands and Settings, shown after the first-run welcome.</summary>
public partial class TourWindow : Window
{
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly AppSettings _settings;
    private int _index;

    public TourWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _steps = TourSteps.Build(settings.AssistantName);
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        _index = index;
        var step = _steps[index];
        StepCount.Text = $"Step {index + 1} of {_steps.Count}";
        StepTitle.Text = step.Title;
        StepIntro.Text = step.Intro;

        Rows.Children.Clear();
        foreach (var row in step.Rows) Rows.Children.Add(BuildRow(row));

        BackButton.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = index == _steps.Count - 1 ? "Done" : "Next";
    }

    private UIElement BuildRow(TourRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keys = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("Surface"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = row.Keys, Foreground = (System.Windows.Media.Brush)FindResource("Fg"), FontSize = 12, FontWeight = FontWeights.SemiBold }
        };
        var text = new TextBlock
        {
            Text = row.Text,
            Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(keys);
        grid.Children.Add(text);
        return grid;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _steps.Count - 1) ShowStep(_index + 1);
        else Finish();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(Math.Max(0, _index - 1));

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish();

    // Closing with the X counts as skipping, so the tour doesn't nag.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_settings.TourDone)
        {
            _settings.TourDone = true;
            _settings.Save();
        }
    }

    private void Finish() => Close();
}
