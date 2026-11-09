using System.Runtime.InteropServices;

namespace RobloxModManager.UI;

/// <summary>Holding Shift while Roblox launches skips mods and fast flags for that one launch.</summary>
public static class SafeMode
{
    private const int VkShift = 0x10;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>Checks the physical key, so it works even though no window of ours has focus yet.</summary>
    public static bool IsShiftHeld()
    {
        try { return (GetAsyncKeyState(VkShift) & 0x8000) != 0; }
        catch { return false; }
    }
}
