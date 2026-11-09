using System.Windows;
using System.Windows.Controls;

namespace RobloxModManager.UI.Controls;

/// <summary>
/// A settings row: bold title and grey description on the left, the control (toggle, combo box, button) on the right.
/// Its look lives in Themes/Controls.xaml.
/// </summary>
public class OptionControl : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(OptionControl), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(OptionControl), new PropertyMetadata(""));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
