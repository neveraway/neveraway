using System.Diagnostics;

namespace NeverAway.Core;

// macOS has no F24, so we use F15 — same key Caffeine uses, present in
// the virtual key code table (kVK_F15 = 113) but never on physical
// keyboards in the wild.
//
// Implementation shells out to osascript ("AppleScript") rather than
// P/Invoking ApplicationServices.framework's CGEventCreate. Reason:
// CGEvent posting on macOS now requires Accessibility permission
// (System Settings > Privacy & Security > Accessibility) and the
// permission grant prompt is awkward to drive from a single-file
// console binary. osascript is preinstalled, no permission flow, and
// the latency cost (~30ms per call) is irrelevant when we fire once
// every 10 seconds.
//
// First call may still trigger a one-time prompt depending on macOS
// version and how the binary was launched. Subsequent calls work.
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacInputSimulator : IInputSimulator
{
    // F15 = key code 113 in the macOS Carbon-era virtual key map. System
    // Events accepts that integer.
    private const int KeyCode = 113;

    public void Tap()
    {
        var psi = new ProcessStartInfo("osascript", $"-e \"tell application \\\"System Events\\\" to key code {KeyCode}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) throw new InvalidOperationException("osascript failed to start");
        p.WaitForExit();
    }
}
