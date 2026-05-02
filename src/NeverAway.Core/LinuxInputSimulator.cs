using System.Diagnostics;

namespace NeverAway.Core;

// Linux: shell out to xdotool (sudo apt install xdotool) or ydotool
// for Wayland. xdotool is the X11 standard. We pick F15 — same as the
// Mac side — for consistency with Caffeine's convention.
//
// If xdotool isn't installed, Tap() throws on first call. Linux users
// can install via package manager (xdotool is in Debian/Ubuntu/Fedora
// repos by default).
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxInputSimulator : IInputSimulator
{
    public void Tap()
    {
        var psi = new ProcessStartInfo("xdotool", "key F15")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "xdotool not found — install with: sudo apt install xdotool");
        p.WaitForExit();
    }
}
