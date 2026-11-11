using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinCompanion.UI;

/// <summary>A small modal box asking for a password (twice when choosing one). The password is returned, never stored or logged.</summary>
internal static class PasswordPrompt
{
    /// <returns>The password; an empty string when the box was left empty and that is allowed; null when cancelled.</returns>
    public static string? Ask(Window? owner, string title, string message, bool confirm, bool allowEmpty, string? error = null,
        Func<string, string?>? validate = null)
    {
        Func<string, object> res = key => Application.Current.FindResource(key);
        string? result = null;

        var first = new PasswordBox { Style = (Style)res("PasswordField") };
        var second = confirm ? new PasswordBox { Style = (Style)res("PasswordField"), Margin = new Thickness(0, 8, 0, 0) } : null;
        var problem = new TextBlock
        {
            Foreground = (System.Windows.Media.Brush)res("ErrorText"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0), Text = error ?? "", Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible
        };

        var ok = new Button { Content = "OK", Style = (Style)res("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Style = (Style)res("GhostButton"), IsCancel = true };

        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = (System.Windows.Media.Brush)res("WindowBack"),
            FontFamily = (System.Windows.Media.FontFamily)res("UiFont")
        };

        void Show(string text)
        {
            problem.Text = text;
            problem.Visibility = Visibility.Visible;
        }

        ok.Click += (_, _) =>
        {
            var password = first.Password;
            if (password.Length == 0 && !allowEmpty) { Show("Type a password."); return; }
            if (validate?.Invoke(password) is { } why) { Show(why); return; }
            if (second is not null && password != second.Password) { Show("The two passwords don't match."); return; }
            result = password;
            window.DialogResult = true;
        };

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = message, Foreground = (System.Windows.Media.Brush)res("Fg"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(first);
        if (second is not null) panel.Children.Add(second);
        panel.Children.Add(problem);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel } });
        window.Content = panel;
        window.Loaded += (_, _) => first.Focus();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.DialogResult = false; };

        return window.ShowDialog() == true ? result : null;
    }
}
