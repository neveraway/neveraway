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
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgD(IntPtr o, IntPtr s, double a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void MsgVL(IntPtr o, IntPtr s, long a);

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

    [UnmanagedCallersOnly]
    private static void OnTogglePressed(IntPtr self, IntPtr cmd)
    {
        _isActive = !_isActive;
        if (_toggleItem != IntPtr.Zero)
            Msg(_toggleItem, Sel("setTitle:"), NSString(_isActive ? "Pause" : "Resume"));
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

        // Status item with variable length, coffee glyph as title
        var statusBar = Msg(Cls("NSStatusBar"), Sel("systemStatusBar"));
        var statusItem = MsgD(statusBar, Sel("statusItemWithLength:"), -1.0); // NSVariableStatusItemLength
        var button = Msg(statusItem, Sel("button"));
        Msg(button, Sel("setTitle:"), NSString("☕")); // ☕
        Msg(button, Sel("setToolTip:"), NSString("NeverAway"));

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
        var sim = new MacInputSimulator();
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
