using System.Diagnostics;

namespace NeverAway.Core;

// macOS has no F24, so we use an obscure F-key from the virtual key
// table that doesn't exist on physical keyboards. F15 (key code 113)
// is what Caffeine traditionally used, but on some Mac configurations
// macOS interprets F15 as "brightness up" -- the keypress becomes
// visible. F19 (key code 80) is much safer: not on any keyboard and
// no default system mapping.
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
    // F19 = key code 80 (kVK_F19) in the macOS Carbon virtual key map.
    // System Events accepts that integer.
    private const int KeyCode = 80;

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
