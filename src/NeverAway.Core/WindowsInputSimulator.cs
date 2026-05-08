using System.Runtime.InteropServices;

namespace NeverAway.Core;

// user32.dll keybd_event for F24 — direct port of the v2 implementation.
// F24 is in the virtual-key code map (VK_F24 = 0x87) but no modern
// physical keyboard has it, so apps watching for keys never react.
//
// SupportedOSPlatform attribute makes the analyzer warn if this gets
// instantiated on non-Windows — InputSimulator.ForCurrentOs() guards
// against that, but the attribute documents intent.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsInputSimulator : IInputSimulator
{
    private const byte VK_F24 = 0x87;
    private const int KEY_UP_EVENT = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    public void Tap() => keybd_event(VK_F24, 0, KEY_UP_EVENT, 0);
}
