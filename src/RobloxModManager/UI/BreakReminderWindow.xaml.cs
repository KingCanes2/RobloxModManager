using System.Windows;
using System.Windows.Threading;

namespace RobloxModManager.UI;

/// <summary>A small corner notice that doesn't take focus away from the game.</summary>
public partial class BreakReminderWindow : Window
{
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(25) };

    /// <summary>True if the user asked to be reminded again sooner.</summary>
    public bool Snoozed { get; private set; }

    public BreakReminderWindow(string game, TimeSpan played)
    {
        InitializeComponent();

        HeadingText.Text = "Time for a break?";
        MessageText.Text = $"You've been playing {game} for {Core.PlayHistory.Format(played)}. Stretch, get some water, rest your eyes.";

        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        };

        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Snoozed = true;
        Close();
    }
}
