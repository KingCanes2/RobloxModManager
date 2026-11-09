using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

/// <summary>Edits GlobalBasicSettings_13.xml through <see cref="GlobalSettingsManager"/>.</summary>
public partial class EngineSettingsPage : UserControl, ISettingsPage
{
    private readonly SettingsContext _context;
    private bool _loading;

    private GlobalSettingsManager Global => _context.Global;

    public EngineSettingsPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        bool loaded = Global.Loaded;
        MissingBanner.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;

        if (!loaded)
            return;

        _loading = true;

        ReadOnlyToggle.IsChecked = _context.GlobalReadOnly;

        QualitySlider.Value = Math.Clamp(ParseInt(Global.Get("Rendering.SavedQualityLevel"), 0), 0, 10);
        QualityValue.Text = QualityLabel((int)QualitySlider.Value);

        FramerateBox.Text = Global.Get("Rendering.FramerateCap") ?? "60";

        TransparencySlider.Value = Math.Clamp(ParseDouble(Global.Get("UI.Transparency"), 1), 0, 1);
        TransparencyValue.Text = $"{TransparencySlider.Value:P0}";

        ReducedMotionToggle.IsChecked = IsTrue(Global.Get("UI.ReducedMotion"));
        TextSizeBox.SelectedIndex = Math.Clamp(ParseInt(Global.Get("UI.FontSize"), 1), 1, 4) - 1;

        SensitivityBox.Text = Global.Get("User.MouseSensitivity") ?? "1";
        VrToggle.IsChecked = IsTrue(Global.Get("User.VREnabled"));

        _loading = false;
    }

    // ------------------------------------------------------------------ helpers

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : fallback;

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string QualityLabel(int level) => level == 0 ? "Automatic" : level.ToString(CultureInfo.CurrentCulture);

    private void Set(string preset, string value)
    {
        if (_loading)
            return;

        Global.Set(preset, value);
        _context.GlobalDirty = true;
        _context.MarkChanged();
    }

    private void MarkValid(TextBox box, bool valid)
    {
        if (valid)
            box.ClearValue(BorderBrushProperty);
        else
            box.SetResourceReference(BorderBrushProperty, "DangerBrush");
    }

    // ------------------------------------------------------------------ events

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!Global.CreateTemplate())
        {
            MessageDialog.Error("Couldn't create the settings file",
                $"Check that {Paths.Roblox} exists and isn't read-only, then try again.");
            return;
        }

        _context.GlobalReadOnly = Global.IsReadOnly;
        OnShown();
    }

    private void ReadOnly_Click(object sender, RoutedEventArgs e)
    {
        _context.GlobalReadOnly = ReadOnlyToggle.IsChecked == true;
        _context.MarkChanged();
    }

    private void Quality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValue is null)
            return;

        int level = (int)QualitySlider.Value;
        QualityValue.Text = QualityLabel(level);
        Set("Rendering.SavedQualityLevel", level.ToString(CultureInfo.InvariantCulture));
    }

    private void Framerate_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool valid = int.TryParse(FramerateBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps)
                     && fps is >= 1 and <= 10000;

        MarkValid(FramerateBox, valid);

        if (valid)
            Set("Rendering.FramerateCap", fps.ToString(CultureInfo.InvariantCulture));
    }

    private void Transparency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransparencyValue is null)
            return;

        double value = Math.Round(TransparencySlider.Value, 1);
        TransparencyValue.Text = $"{value:P0}";
        Set("UI.Transparency", value.ToString(CultureInfo.InvariantCulture));
    }

    private void ReducedMotion_Click(object sender, RoutedEventArgs e) =>
        Set("UI.ReducedMotion", ReducedMotionToggle.IsChecked == true ? "true" : "false");

    private void TextSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TextSizeBox.SelectedIndex >= 0)
            Set("UI.FontSize", (TextSizeBox.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture));
    }

    private void Sensitivity_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = SensitivityBox.Text.Trim().Replace(',', '.');
        bool valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                     && value is >= 0.1 and <= 10;

        MarkValid(SensitivityBox, valid);

        if (valid)
            Set("User.MouseSensitivity", value.ToString(CultureInfo.InvariantCulture));
    }

    private void Vr_Click(object sender, RoutedEventArgs e) =>
        Set("User.VREnabled", VrToggle.IsChecked == true ? "true" : "false");
}
