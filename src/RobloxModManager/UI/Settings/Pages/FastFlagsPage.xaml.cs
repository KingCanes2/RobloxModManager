using System.Windows;
using System.Windows.Controls;
using RobloxModManager.Core;
using RobloxModManager.UI.Dialogs;

namespace RobloxModManager.UI.Settings.Pages;

public partial class FastFlagsPage : UserControl, ISettingsPage
{
    private static readonly string?[] MsaaValues = { null, "1", "2", "4" };
    private static readonly string[] MeshLabels = { "Lowest", "Low", "High", "Highest" };

    private readonly SettingsContext _context;
    private bool _loading;

    private FastFlagManager Flags => _context.Flags;

    public FastFlagsPage(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
    }

    public void OnShown()
    {
        _loading = true;

        ManagerToggle.IsChecked = _context.Settings.UseFastFlagManager;

        RenderingModeBox.SelectedIndex =
            Flags.GetPreset("Rendering.Mode.D3D11") == "True" ? 1 :
            Flags.GetPreset("Rendering.Mode.Vulkan") == "True" ? 2 : 0;

        int msaa = Array.IndexOf(MsaaValues, Flags.GetPreset("Rendering.MSAA"));
        MsaaBox.SelectedIndex = msaa < 0 ? 0 : msaa;

        ScalingToggle.IsChecked = Flags.GetPreset("Rendering.DisableScaling") == "True";
        ExclusiveFullscreenToggle.IsChecked = Flags.GetPreset("Rendering.ManualFullscreen") == "False";

        string? frm = Flags.GetPreset("Rendering.FRMQualityOverride");
        FrmToggle.IsChecked = frm is not null;
        FrmSlider.Value = int.TryParse(frm, out int frmLevel) ? Math.Clamp(frmLevel, 1, 21) : 21;
        FrmSlider.IsEnabled = frm is not null;
        FrmValue.Text = ((int)FrmSlider.Value).ToString();

        string? mesh = Flags.GetPreset("Geometry.MeshLOD.Static");
        MeshToggle.IsChecked = mesh is not null;
        MeshSlider.Value = int.TryParse(mesh, out int meshLevel) ? Math.Clamp(meshLevel, 0, 3) : 3;
        MeshSlider.IsEnabled = mesh is not null;
        MeshValue.Text = MeshLabels[(int)MeshSlider.Value];

        _loading = false;
    }

    private void Changed() => _context.MarkChanged();

    private void Manager_Click(object sender, RoutedEventArgs e)
    {
        _context.Settings.UseFastFlagManager = ManagerToggle.IsChecked == true;
        Changed();
    }

    private void Editor_Click(object sender, RoutedEventArgs e) => _context.Navigate(SettingsPages.FastFlagEditor);

    private void RenderingMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        Flags.SetPreset("Rendering.Mode.D3D11", RenderingModeBox.SelectedIndex == 1 ? "True" : null);
        Flags.SetPreset("Rendering.Mode.Vulkan", RenderingModeBox.SelectedIndex == 2 ? "True" : null);
        Changed();
    }

    private void Msaa_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MsaaBox.SelectedIndex < 0)
            return;

        Flags.SetPreset("Rendering.MSAA", MsaaValues[MsaaBox.SelectedIndex]);
        Changed();
    }

    private void Scaling_Click(object sender, RoutedEventArgs e)
    {
        Flags.SetPreset("Rendering.DisableScaling", ScalingToggle.IsChecked == true ? "True" : null);
        Changed();
    }

    private void ExclusiveFullscreen_Click(object sender, RoutedEventArgs e)
    {
        Flags.SetPreset("Rendering.ManualFullscreen", ExclusiveFullscreenToggle.IsChecked == true ? "False" : null);
        Changed();
    }

    private void FrmToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = FrmToggle.IsChecked == true;
        FrmSlider.IsEnabled = on;
        Flags.SetPreset("Rendering.FRMQualityOverride", on ? ((int)FrmSlider.Value).ToString() : null);
        Changed();
    }

    private void Frm_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrmValue is null)
            return; // raised while the page is still being built

        FrmValue.Text = ((int)FrmSlider.Value).ToString();

        if (_loading || FrmToggle.IsChecked != true)
            return;

        Flags.SetPreset("Rendering.FRMQualityOverride", ((int)FrmSlider.Value).ToString());
        Changed();
    }

    private void MeshToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = MeshToggle.IsChecked == true;
        MeshSlider.IsEnabled = on;

        if (on)
            SetMeshQuality((int)MeshSlider.Value);
        else
        {
            foreach (string level in FastFlagManager.MeshLodLevels)
                Flags.SetPreset($"Geometry.MeshLOD.{level}", null);

            Flags.SetPreset("Geometry.MeshLOD.Static", null);
        }

        Changed();
    }

    private void Mesh_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MeshValue is null)
            return;

        MeshValue.Text = MeshLabels[Math.Clamp((int)MeshSlider.Value, 0, 3)];

        if (_loading || MeshToggle.IsChecked != true)
            return;

        SetMeshQuality((int)MeshSlider.Value);
        Changed();
    }

    /// <summary>Same distances Fishstrap uses: each level of detail switches 250 studs further out per quality step.</summary>
    private void SetMeshQuality(int quality)
    {
        quality = Math.Clamp(quality, 0, FastFlagManager.MeshLodLevels.Length - 1);

        for (int i = 0; i < FastFlagManager.MeshLodLevels.Length; i++)
        {
            int distance = (Math.Clamp(quality - i, 0, 3) + 1) * 250;
            Flags.SetPreset($"Geometry.MeshLOD.{FastFlagManager.MeshLodLevels[i]}", distance.ToString());
        }

        Flags.SetPreset("Geometry.MeshLOD.Static", quality.ToString());
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Flags.Flags.Count == 0)
        {
            MessageDialog.Info("Nothing to reset", "No fast flags are set.");
            return;
        }

        if (!MessageDialog.Confirm("Reset all fast flags?",
                $"This removes all {Flags.Flags.Count} flags, including ones added in the editor. Nothing changes until you save.",
                "Reset", "Cancel", DialogKind.Warning))
            return;

        Flags.Clear();
        OnShown();
        Changed();
    }
}
