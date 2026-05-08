using System.Runtime.InteropServices;
using NeverAway.Core;

namespace NeverAway.Mac;

// CGEvent-based F19 keypress via raw P/Invoke into ApplicationServices.
// F19 (kVK_F19 = 80) is the highest-safe macOS virtual key code -- not
// on any physical keyboard, no system mapping. Same key the Console
// runner uses, but posted via CGEvent directly instead of shelling
// out to osascript.
//
// Why this approach:
//   - Single permission category: Accessibility (vs osascript's
//     Automation prompt). One unified prompt for the .app bundle.
//   - No process spawn per tick (~30ms savings, irrelevant at 10s
//     intervals but still cleaner).
//   - No Xcode / macos workload needed -- raw P/Invoke into a system
//     framework is plain net10.0.
//
// First call triggers the macOS Accessibility permission prompt:
//   "NeverAway wants to control your computer using accessibility..."
// User clicks through to System Settings > Privacy & Security >
// Accessibility, toggles NeverAway on. Subsequent runs work without
// further prompts.
public sealed class MacInputSimulator : IInputSimulator
{
    private const ushort KVK_F19 = 80;
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr ev);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    // CGEventTapLocation.HID = 0 (lowest in the chain, all observers see it)
    private const uint TapLocationHid = 0;

    public void Tap()
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, KVK_F19, true);
        if (down != IntPtr.Zero)
        {
            CGEventPost(TapLocationHid, down);
            CFRelease(down);
        }

        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, KVK_F19, false);
        if (up != IntPtr.Zero)
        {
            CGEventPost(TapLocationHid, up);
            CFRelease(up);
        }
    }
}
