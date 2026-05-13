using System.Runtime.InteropServices;
using NeverAway.Core;

namespace NeverAway.Mac;

// Belt-and-suspenders user-activity simulation on macOS.
//
// Three layers, each addressing a different idle counter:
//
//   1. Persistent IOPM assertions (created once at first Tap()):
//        - PreventUserIdleDisplaySleep -- declares display-sleep is off
//        - PreventUserIdleSystemSleep  -- declares system-sleep is off
//      These are held continuously while the app runs. Visible in
//      `pmset -g assertions`.
//
//   2. IOPMAssertionDeclareUserActivity refreshed each Tap()
//      (kIOPMUserActiveLocal). Documented to "reset the system idle
//      timer." Apple Developer Forums thread #26776 reports this does
//      NOT prevent screen-saver activation; we include it anyway as a
//      cheap layer in case the per-system behavior differs.
//
//   3. Zero-pixel CGEventCreateMouseEvent at the current cursor
//      position (the v3.0.1 mouse-jiggle). Empirically confirmed to
//      reset HIDIdleTime in a sawtooth pattern and to trigger
//      WindowServer-mediated UserIsActive assertions (visible in
//      `pmset -g assertions` named with pid:<NeverAway>). This is what
//      keeps Teams/Slack from going Away and what suppressed display
//      dimming in v3.0.0 (with F19) and v3.0.1-pre (with mouse-jiggle).
//
// Empirical finding from diagnostics 2026-05-12: layer 3 alone is not
// sufficient to prevent screen lock on modern macOS. The lock timer
// appears to track real-hardware events via AppleUserHIDEventService
// separately from synthetic events via IOHIDSystem. Layers 1+2 may
// fill that gap; if not, screen-lock prevention is genuinely
// unavailable from userspace on modern macOS and we'll document.

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
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOKit =
        "/System/Library/Frameworks/IOKit.framework/IOKit";

    // CGEvent constants
    private const uint CGEventMouseMoved = 5;
    private const uint CGMouseButtonLeft = 0;
    private const uint TapLocationHid = 0;

    // CFString encoding for assertion names / types
    private const uint CFStringEncodingUTF8 = 0x08000100;

    // IOPM constants
    private const uint IOPMAssertionLevelOn = 255;
    private const int IOPMUserActiveLocal = 0;

    // --- CGEvent P/Invokes (mouse-jiggle layer) ---

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(ApplicationServices)]
    private static extern CGPoint CGEventGetLocation(IntPtr ev);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint position, uint button);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr ev);

    // --- CFString P/Invoke (assertion name/type strings) ---

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    // --- IOPM P/Invokes (assertion layer) ---

    [DllImport(IOKit)]
    private static extern int IOPMAssertionCreateWithName(IntPtr assertionType, uint level, IntPtr name, out uint assertionId);

    [DllImport(IOKit)]
    private static extern int IOPMAssertionDeclareUserActivity(IntPtr name, int userType, ref uint assertionId);

    // --- Persistent state for the IOPM assertions ---

    private static IntPtr _appName;
    private static IntPtr _displaySleepType;
    private static IntPtr _systemSleepType;
    private static uint _displaySleepAssertion;
    private static uint _systemSleepAssertion;
    private static uint _userActiveAssertion;
    private static bool _persistentReady;

    private static void EnsurePersistentAssertions()
    {
        if (_persistentReady) return;

        _appName = CFStringCreateWithCString(IntPtr.Zero, "NeverAway", CFStringEncodingUTF8);
        _displaySleepType = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleDisplaySleep", CFStringEncodingUTF8);
        _systemSleepType = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleSystemSleep", CFStringEncodingUTF8);

        // Held for app lifetime. Visible in `pmset -g assertions`. Non-fatal
        // if either fails -- the mouse-jiggle layer below still functions.
        IOPMAssertionCreateWithName(_displaySleepType, IOPMAssertionLevelOn, _appName, out _displaySleepAssertion);
        IOPMAssertionCreateWithName(_systemSleepType, IOPMAssertionLevelOn, _appName, out _systemSleepAssertion);

        _persistentReady = true;
    }

    public void Tap()
    {
        EnsurePersistentAssertions();

        // Layer 2: refresh "user activity" declaration. assertionId is
        // refreshed in-place (0 first time = create new; subsequent calls
        // refresh the same assertion).
        IOPMAssertionDeclareUserActivity(_appName, IOPMUserActiveLocal, ref _userActiveAssertion);

        // Layer 3: zero-pixel mouse-move at current cursor position.
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
