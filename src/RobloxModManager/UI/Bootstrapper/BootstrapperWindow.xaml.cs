using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Bootstrapper;

/// <summary>The window shown while Roblox is updated and started. Safe to update from any thread.</summary>
public partial class BootstrapperWindow : Window, IBootstrapperUi
{
    private const int TerminalMaxChars = 200_000;

    private readonly bool _preview;
    private readonly Action<string>? _logHandler;
    private bool _finished;
    private DispatcherTimer? _previewTimer;

    /// <summary>Raised when the user presses Cancel or closes the window.</summary>
    public event Action? CancelRequested;

    public BootstrapperWindow(BootstrapperStyle style, string title, bool preview = false)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _preview = preview;
        Title = string.IsNullOrWhiteSpace(title) ? AppInfo.DisplayName : title;

        FluentPanel.Visibility = style == BootstrapperStyle.Fluent ? Visibility.Visible : Visibility.Collapsed;
        ClassicPanel.Visibility = style == BootstrapperStyle.Classic ? Visibility.Visible : Visibility.Collapsed;
        TerminalPanel.Visibility = style == BootstrapperStyle.Terminal ? Visibility.Visible : Visibility.Collapsed;

        if (style == BootstrapperStyle.Classic)
        {
            Width = 420;
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
        }
        else if (style == BootstrapperStyle.Terminal)
        {
            Width = 680;
            Background = (Brush)FindResource("TerminalBackgroundBrush");

            if (!preview)
            {
                _logHandler = line => Dispatcher.InvokeAsync(() => AppendTerminal(line));
                Logger.LineWritten += _logHandler;
            }
        }

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            if (_logHandler is not null)
                Logger.LineWritten -= _logHandler;

            _previewTimer?.Stop();
        };
    }

    // ------------------------------------------------------------------ IBootstrapperUi

    public void SetStatus(string message) => Dispatcher.InvokeAsync(() =>
    {
        FluentStatus.Text = message;
        ClassicStatus.Text = message;
        TerminalStatus.Text = message;

        if (_preview && TerminalPanel.Visibility == Visibility.Visible)
            AppendTerminal(message);
    });

    public void SetProgress(int percent) => Dispatcher.InvokeAsync(() =>
    {
        bool indeterminate = percent < 0;

        foreach (ProgressBar bar in new[] { FluentProgress, ClassicProgress })
        {
            bar.IsIndeterminate = indeterminate;
            if (!indeterminate)
                bar.Value = percent;
        }

        Taskbar.ProgressState = indeterminate ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
        Taskbar.ProgressValue = indeterminate ? 0 : percent / 100.0;
    });

    public void SetCancelEnabled(bool enabled) => Dispatcher.InvokeAsync(() =>
    {
        FluentCancel.IsEnabled = enabled;
        ClassicCancel.IsEnabled = enabled;
        TerminalCancel.IsEnabled = enabled;
    });

    public bool Confirm(string message) =>
        Dispatcher.Invoke(() => MessageDialog.Confirm(AppInfo.DisplayName, message));

    // ------------------------------------------------------------------ closing

    /// <summary>Closes the window for good (call from the UI thread).</summary>
    public void Finish()
    {
        if (_finished)
            return;

        _finished = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_finished || _preview)
            return;

        // Closing the window means "cancel"; the launch flow closes it once cancellation finishes.
        e.Cancel = true;
        RequestCancel();
    }

    private void RequestCancel()
    {
        if (_preview)
        {
            Finish();
            return;
        }

        if (!FluentCancel.IsEnabled)
            return;

        SetCancelEnabled(false);
        SetStatus("Cancelling...");
        CancelRequested?.Invoke();
    }

    private void AppendTerminal(string line)
    {
        TerminalLog.AppendText(line + Environment.NewLine);

        if (TerminalLog.Text.Length > TerminalMaxChars)
            TerminalLog.Text = TerminalLog.Text[^(TerminalMaxChars / 2)..];

        TerminalLog.ScrollToEnd();
    }

    // ------------------------------------------------------------------ preview

    /// <summary>Shows the window with simulated progress so the style can be checked from settings.</summary>
    public static void ShowPreview(BootstrapperStyle style, string title, Window? owner)
    {
        var window = new BootstrapperWindow(style, title, preview: true);

        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        int step = 0;
        window._previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        window._previewTimer.Tick += (_, _) =>
        {
            step++;

            if (step == 1) { window.SetStatus("Connecting to Roblox..."); window.SetProgress(-1); }
            else if (step == 20) { window.SetStatus("Installing Roblox..."); }
            else if (step > 20 && step <= 120) { window.SetProgress(step - 20); }
            else if (step == 121) { window.SetStatus("Applying modifications..."); window.SetProgress(-1); }
            else if (step == 150) { window.SetStatus("Starting Roblox..."); }
            else if (step == 190) { step = 0; } // loop until the user closes it
        };

        window.Show();
        window._previewTimer.Start();
    }
}
