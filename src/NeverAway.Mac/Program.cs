using System.Runtime.InteropServices;

namespace NeverAway.Mac;

// Menu-bar app via raw P/Invoke into the Objective-C runtime + AppKit.
// No net10.0-macos workload, no Xcode -- just net10.0 + libobjc + the
// system AppKit framework loaded at runtime on the target Mac.
//
// Layout matches the Windows tray UX as closely as macOS conventions
// allow: status item with a coffee glyph in the menu bar, dropdown
// menu with Pause/Resume + Quit, tap loop on threadpool.
internal static class Program
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string AppServicesPath = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string IOKitPath = "/System/Library/Frameworks/IOKit.framework/IOKit";

    [DllImport(LibSystem)] private static extern IntPtr dlopen(string path, int mode);
    private const int RTLD_NOW = 2;

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(LibObjC)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(LibObjC)] private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr o, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, IntPtr b);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgD(IntPtr o, IntPtr s, double a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void MsgVL(IntPtr o, IntPtr s, long a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgB(IntPtr o, IntPtr s, byte b);

    // Accessibility check + auto-prompt. Documented in <ApplicationServices/AXUIElement.h>.
    // Pass a CFDictionary with kAXTrustedCheckOptionPrompt=YES to fire the system prompt
    // when the process isn't yet trusted. NSDictionary is toll-free bridged to CFDictionary.
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    private static IntPtr Cls(string n) => objc_getClass(n);
    private static IntPtr Sel(string n) => sel_registerName(n);

    // Allocate + init an NSString from a managed string.
    private static IntPtr NSString(string s)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try
        {
            var alloc = Msg(Cls("NSString"), Sel("alloc"));
            return Msg(alloc, Sel("initWithUTF8String:"), utf8);
        }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    // Toggle/Quit state -- read by both UI thread and tap-loop thread.
    private static volatile bool _isActive = true;
    private static IntPtr _toggleItem;
    private static IntPtr _statusButton;
    private static MacInputSimulator? _sim;

    // Match the Windows tray's deliberate icon choice:
    //   active   = SystemIcons.Error (red alert)        -> "no entry" ⛔
    //   inactive = SystemIcons.Shield (security/paused) -> shield     🛡
    private const string IconActive = "⛔";   // ⛔
    private const string IconInactive = "🛡️"; // 🛡 with VS-16

    [UnmanagedCallersOnly]
    private static void OnTogglePressed(IntPtr self, IntPtr cmd)
    {
        _isActive = !_isActive;
        if (_toggleItem != IntPtr.Zero)
            Msg(_toggleItem, Sel("setTitle:"), NSString(_isActive ? "Pause" : "Resume"));
        if (_statusButton != IntPtr.Zero)
            Msg(_statusButton, Sel("setTitle:"), NSString(_isActive ? IconActive : IconInactive));
        // On Pause, release IOPM assertions so the OS resumes normal idle
        // behavior (display sleep, screen lock can fire). On Resume, the
        // next Tap() re-creates them via EnsurePersistentAssertions().
        if (!_isActive)
            _sim?.ReleaseAllAssertions();
    }

    [UnmanagedCallersOnly]
    private static void OnQuitPressed(IntPtr self, IntPtr cmd)
    {
        var app = Msg(Cls("NSApplication"), Sel("sharedApplication"));
        Msg(app, Sel("terminate:"), IntPtr.Zero);
    }

    private static void Main()
    {
        // Load AppKit explicitly. Without this, objc_getClass("NSApplication")
        // returns nil because we never link against AppKit at compile time --
        // dotnet only knows about the system frameworks we explicitly load.
        if (dlopen(AppKitPath, RTLD_NOW) == IntPtr.Zero)
            throw new InvalidOperationException($"failed to dlopen {AppKitPath}");
        if (dlopen(AppServicesPath, RTLD_NOW) == IntPtr.Zero)
            throw new InvalidOperationException($"failed to dlopen {AppServicesPath}");
        if (dlopen(IOKitPath, RTLD_NOW) == IntPtr.Zero)
            throw new InvalidOperationException($"failed to dlopen {IOKitPath}");

        // Trigger the macOS Accessibility permission prompt at startup --
        // but only if we're actually untrusted. The naive single-call
        // AXIsProcessTrustedWithOptions(prompt:YES) approach fires the
        // prompt on every launch for ad-hoc-signed bundles even when
        // System Settings shows the toggle as already on (Apple API
        // quirk or trust-DB race on first launch). Two-step: check
        // without prompting first; only prompt if the silent check
        // returns false. Avoids the every-launch prompt UX while still
        // surfacing the dialog on truly-first-grant.
        var promptKey = NSString("AXTrustedCheckOptionPrompt");
        var falseVal = MsgB(Cls("NSNumber"), Sel("numberWithBool:"), 0);
        var silentCheckOptions = Msg(Cls("NSDictionary"), Sel("dictionaryWithObject:forKey:"), falseVal, promptKey);
        bool alreadyTrusted = AXIsProcessTrustedWithOptions(silentCheckOptions);
        if (!alreadyTrusted)
        {
            var trueVal = MsgB(Cls("NSNumber"), Sel("numberWithBool:"), 1);
            var promptOptions = Msg(Cls("NSDictionary"), Sel("dictionaryWithObject:forKey:"), trueVal, promptKey);
            AXIsProcessTrustedWithOptions(promptOptions);
            // Return value intentionally ignored. If user grants, future taps
            // work. If user dismisses, taps silently fail until they re-launch
            // and grant.
        }

        // Custom NSObject subclass to host the menu-action callbacks.
        // The Objective-C runtime needs a real class with selectors --
        // can't dispatch [target action:] to a raw function pointer.
        var actionClass = objc_allocateClassPair(Cls("NSObject"), "NeverAwayActionTarget", 0);
        IntPtr togglePtr, quitPtr;
        unsafe
        {
            togglePtr = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnTogglePressed;
            quitPtr = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnQuitPressed;
        }
        // type encoding "v@:@" = void return, self (id), cmd (SEL), one id arg
        class_addMethod(actionClass, Sel("toggle:"), togglePtr, "v@:@");
        class_addMethod(actionClass, Sel("quit:"), quitPtr, "v@:@");
        objc_registerClassPair(actionClass);
        var actionTarget = Msg(Msg(actionClass, Sel("alloc")), Sel("init"));

        // NSApplication.sharedApplication, set as Accessory (menu-bar only)
        var app = Msg(Cls("NSApplication"), Sel("sharedApplication"));
        MsgVL(app, Sel("setActivationPolicy:"), 1L); // NSApplicationActivationPolicyAccessory

        // Status item with variable length, icon as title (see IconActive/IconInactive)
        var statusBar = Msg(Cls("NSStatusBar"), Sel("systemStatusBar"));
        var statusItem = MsgD(statusBar, Sel("statusItemWithLength:"), -1.0); // NSVariableStatusItemLength
        _statusButton = Msg(statusItem, Sel("button"));
        Msg(_statusButton, Sel("setTitle:"), NSString(IconActive));
        Msg(_statusButton, Sel("setToolTip:"), NSString("NeverAway"));

        // Menu: Pause / -- / Quit
        var menu = Msg(Msg(Cls("NSMenu"), Sel("alloc")), Sel("init"));

        var pauseItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(pauseItem, Sel("setTitle:"), NSString("Pause"));
        Msg(pauseItem, Sel("setAction:"), Sel("toggle:"));
        Msg(pauseItem, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), pauseItem);
        _toggleItem = pauseItem;

        Msg(menu, Sel("addItem:"), Msg(Cls("NSMenuItem"), Sel("separatorItem")));

        var quitItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(quitItem, Sel("setTitle:"), NSString("Quit NeverAway"));
        Msg(quitItem, Sel("setAction:"), Sel("quit:"));
        Msg(quitItem, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), quitItem);

        Msg(statusItem, Sel("setMenu:"), menu);

        // Tap loop on threadpool. CGEvent posting is thread-safe.
        // Stored in static _sim so the toggle handler can call
        // ReleaseAllAssertions() when paused.
        _sim = new MacInputSimulator();
        var sim = _sim;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                if (_isActive)
                {
                    try { sim.Tap(); }
                    catch { /* one bad tap shouldn't kill the loop */ }
                }
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        });

        // Run the main runloop -- blocks until terminate:
        Msg(app, Sel("run"));
    }
}
