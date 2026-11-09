using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;

namespace RobloxModManager.UI.Dialogs;

public enum DialogKind { Info, Warning, Error, Question }

/// <summary>Themed replacement for MessageBox. The first button is the default (Enter), the last is Esc.</summary>
public partial class MessageDialog : Window
{
    private int _result = -1;

    private MessageDialog(string heading, string message, DialogKind kind, string[] buttons, string? details)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        Title = AppInfo.DisplayName;
        HeadingText.Text = heading;
        MessageText.Text = message;

        (string glyph, string brush) = kind switch
        {
            DialogKind.Warning => ("", "WarningBrush"),
            DialogKind.Error => ("", "DangerBrush"),
            DialogKind.Question => ("", "AccentBrush"),
            _ => ("", "AccentBrush")
        };

        IconGlyph.Text = glyph;
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsPanel.Visibility = Visibility.Visible;
            DetailsText.Text = details;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 96,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = i == buttons.Length - 1
            };

            if (i == 0)
                button.Style = (Style)FindResource("AccentButton");

            button.Click += (_, _) =>
            {
                _result = index;
                Close();
            };

            ButtonPanel.Children.Add(button);
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        bool show = DetailsText.Visibility != Visibility.Visible;
        DetailsText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailsButton.Content = show ? "Hide details" : "Show details";
    }

    /// <summary>Shows the dialog and returns the index of the button pressed, or -1 if it was closed.</summary>
    public static int ShowChoice(string heading, string message, DialogKind kind, string? details, params string[] buttons)
    {
        if (buttons.Length == 0)
            buttons = new[] { "OK" };

        var dialog = new MessageDialog(heading, message, kind, buttons, details);

        Window? owner = Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w != dialog && w.IsVisible && w.IsActive);

        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    public static void Info(string heading, string message) =>
        ShowChoice(heading, message, DialogKind.Info, null, "OK");

    public static void Warning(string heading, string message) =>
        ShowChoice(heading, message, DialogKind.Warning, null, "OK");

    public static bool Confirm(string heading, string message, string yes = "Yes", string no = "No", DialogKind kind = DialogKind.Question) =>
        ShowChoice(heading, message, kind, null, yes, no) == 0;

    public static void Error(string heading, string message, Exception? exception = null)
    {
        string? details = null;

        if (exception is not null)
        {
            details = exception.ToString();
            if (Logger.FilePath is not null)
                details += $"\n\nLog file: {Logger.FilePath}";
        }

        ShowChoice(heading, message, DialogKind.Error, details, "OK");
    }
}
