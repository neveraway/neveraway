using System.Runtime.InteropServices;
using NeverAway.Core;

namespace NeverAway.Mac;

// CGEvent-based zero-pixel mouse-move via raw P/Invoke into
// ApplicationServices.
//
// v3.0.0 used a synthetic F19 keypress via CGEventCreateKeyboardEvent +
// CGEventPost. On modern macOS, synthetic keyboard events reset the HID
// idle counter (so display dim / sleep is correctly prevented) but do
// NOT reset the screen-saver idle counter. Screen saver activation
// gates the lock screen via "Require password after screen saver begins
// or display is turned off" in System Settings, so the lock fires even
// while we're "tapping" the keyboard. Power-assertion APIs
// (IOPMAssertionDeclareUserActivity, IOPMAssertionCreateWithName) have
// the same limitation -- they prevent display sleep but not screen
// saver. The known-working approach is a synthetic mouse-move event,
// same shape Amphetamine's "automated mouse cursor movement" feature
// uses to fill the gap.
//
// We probe the current cursor position via CGEventCreate + CGEventGetLocation,
// then post a mouse-moved event at the same coordinates. Zero-pixel
// delta -- the cursor doesn't actually move, but the screen-saver idle
// counter resets.
//
// Permission requirement is unchanged: Accessibility. CGEventPost gates
// the same way for keyboard and mouse events.

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;
}

public sealed class MacInputSimulator : IInputSimulator
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // CGEventType.kCGEventMouseMoved = 5
    private const uint CGEventMouseMoved = 5;
    // CGMouseButton.kCGMouseButtonLeft = 0 (ignored for mouse-moved, required by signature)
    private const uint CGMouseButtonLeft = 0;
    // CGEventTapLocation.HID = 0 (lowest in the chain, all observers see it)
    private const uint TapLocationHid = 0;

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(ApplicationServices)]
    private static extern CGPoint CGEventGetLocation(IntPtr ev);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint position, uint button);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr ev);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    public void Tap()
    {
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return;
        var pos = CGEventGetLocation(probe);
        CFRelease(probe);

        var ev = CGEventCreateMouseEvent(IntPtr.Zero, CGEventMouseMoved, pos, CGMouseButtonLeft);
        if (ev != IntPtr.Zero)
        {
            CGEventPost(TapLocationHid, ev);
            CFRelease(ev);
        }
    }
}
