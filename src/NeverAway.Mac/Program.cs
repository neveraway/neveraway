using System.Globalization;
using System.Runtime.InteropServices;
using NeverAway.Core;
using static NeverAway.Core.AutoOffSchedule;

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
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, IntPtr b, IntPtr c);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgD(IntPtr o, IntPtr s, double a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void MsgVL(IntPtr o, IntPtr s, long a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgB(IntPtr o, IntPtr s, byte b);
    // performSelectorOnMainThread:withObject:waitUntilDone: -- last arg is BOOL (byte).
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void MsgPerformOnMain(IntPtr o, IntPtr s, IntPtr a, IntPtr b, byte c);
    // initWithFrame: / setFrame: -- takes a CGRect by value.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgRect(IntPtr o, IntPtr s, CGRect r);
    // runModal -- returns NSInteger (long).
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern long MsgRetL(IntPtr o, IntPtr s);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGSize { public double Width; public double Height; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;
        public CGRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

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
    private static IntPtr _statusItem;       // for setToolTip:
    private static IntPtr _slot1Item;
    private static IntPtr _slot2Item;
    private static IntPtr _cancelScheduleItem;
    private static IntPtr _actionTarget;
    private static MacInputSimulator? _sim;
    private static readonly AutoOffSchedule _schedule = new();

    // Match the Windows tray's deliberate icon choice:
    //   active   = SystemIcons.Error (red alert)        -> "no entry" ⛔
    //   inactive = SystemIcons.Shield (security/paused) -> shield     🛡
    private const string IconActive = "⛔";   // ⛔
    private const string IconInactive = "🛡️"; // 🛡 with VS-16

    // NSControlStateValueOn = 1, NSControlStateValueMixed = -1, NSControlStateValueOff = 0
    private const long NSControlStateOn    = 1;
    private const long NSControlStateMixed = -1;
    private const long NSControlStateOff   = 0;

    // Regular method -- both menu-click and tap-loop auto-off route here.
    // Mirror of Windows TrayApp.Toggle(isAuto).
    private static void Toggle(bool isAuto)
    {
        _isActive = !_isActive;
        if (!_isActive)
        {
            _schedule.Cause = isAuto ? OffCause.Auto : OffCause.Manual;
            // Manual-off clears any pending schedule (the off it was going to
            // trigger already happened). Auto-off keeps the schedule alive so
            // Daily mode can rearm in OnFired.
            if (!isAuto) _schedule.Cancel();
            _sim?.ReleaseAllAssertions();
        }
        else
        {
            _schedule.Cause = OffCause.None;
        }
        if (_toggleItem != IntPtr.Zero)
            Msg(_toggleItem, Sel("setTitle:"), NSString(_isActive ? "Pause" : "Resume"));
        if (_statusButton != IntPtr.Zero)
            Msg(_statusButton, Sel("setTitle:"), NSString(_isActive ? IconActive : IconInactive));
        RefreshScheduleMenu();
    }

    [UnmanagedCallersOnly]
    private static void OnTogglePressed(IntPtr self, IntPtr cmd) => Toggle(isAuto: false);

    // Called via performSelectorOnMainThread: from the tap loop when the
    // schedule fires. Marshals the auto-off onto the main thread so the
    // menu / status-bar updates happen safely.
    [UnmanagedCallersOnly]
    private static void OnAutoOffFired(IntPtr self, IntPtr cmd)
    {
        if (!_isActive) return;
        _schedule.OnFired(DateTime.Now);
        Toggle(isAuto: true);
    }

    [UnmanagedCallersOnly]
    private static void OnSlot1Pressed(IntPtr self, IntPtr cmd)
    {
        _schedule.Cycle(_schedule.Slot1, DateTime.Now);
        RefreshScheduleMenu();
    }

    [UnmanagedCallersOnly]
    private static void OnSlot2Pressed(IntPtr self, IntPtr cmd)
    {
        _schedule.Cycle(_schedule.Slot2, DateTime.Now);
        RefreshScheduleMenu();
    }

    [UnmanagedCallersOnly]
    private static void OnCancelSchedulePressed(IntPtr self, IntPtr cmd)
    {
        _schedule.Cancel();
        RefreshScheduleMenu();
    }

    // Configure dialog: NSAlert with accessoryView containing two labeled
    // text fields, one per slot. Slot 1 (Duration): minutes value. Slot 2
    // (Absolute): HH:MM time-of-day. Kind is fixed per slot to match the
    // Windows defaults; phase 3 could add Kind switching.
    [UnmanagedCallersOnly]
    private static void OnConfigurePressed(IntPtr self, IntPtr cmd)
    {
        var alert = Msg(Msg(Cls("NSAlert"), Sel("alloc")), Sel("init"));
        Msg(alert, Sel("setMessageText:"), NSString("Configure auto-off"));
        Msg(alert, Sel("setInformativeText:"),
            NSString("Slot 1: total minutes to count down (Duration).\nSlot 2: time of day to fire at, HH:MM 24-hr (Absolute)."));
        Msg(alert, Sel("addButtonWithTitle:"), NSString("Save"));
        Msg(alert, Sel("addButtonWithTitle:"), NSString("Cancel"));

        // Accessory view: 320x80 with two label+field pairs stacked vertically
        var accessory = MsgRect(Msg(Cls("NSView"), Sel("alloc")), Sel("initWithFrame:"),
            new CGRect(0, 0, 320, 80));

        IntPtr slot1Label = CreateLabel("Slot 1 (minutes):", new CGRect(0, 50, 130, 22));
        IntPtr slot1Field = CreateTextField(
            ((int)_schedule.Slot1.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture),
            new CGRect(140, 50, 80, 22));

        IntPtr slot2Label = CreateLabel("Slot 2 (HH:MM):", new CGRect(0, 10, 130, 22));
        IntPtr slot2Field = CreateTextField(
            $"{_schedule.Slot2.Value.Hours:D2}:{_schedule.Slot2.Value.Minutes:D2}",
            new CGRect(140, 10, 80, 22));

        Msg(accessory, Sel("addSubview:"), slot1Label);
        Msg(accessory, Sel("addSubview:"), slot1Field);
        Msg(accessory, Sel("addSubview:"), slot2Label);
        Msg(accessory, Sel("addSubview:"), slot2Field);

        Msg(alert, Sel("setAccessoryView:"), accessory);

        long response = MsgRetL(alert, Sel("runModal"));
        // NSAlertFirstButtonReturn = 1000 (Save), NSAlertSecondButtonReturn = 1001 (Cancel)
        if (response != 1000) return;

        var slot1Text = GetTextFieldString(slot1Field);
        var slot2Text = GetTextFieldString(slot2Field);

        if (int.TryParse(slot1Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int min1) && min1 > 0)
            _schedule.Slot1.Value = TimeSpan.FromMinutes(min1);

        if (TimeSpan.TryParseExact(slot2Text, @"h\:mm", CultureInfo.InvariantCulture, out TimeSpan tod1)
            || TimeSpan.TryParseExact(slot2Text, @"hh\:mm", CultureInfo.InvariantCulture, out tod1))
        {
            // Normalize to a 0-23:59 time-of-day. anything outside that range
            // is invalid -- silently ignore (user can re-open).
            if (tod1 >= TimeSpan.Zero && tod1 < TimeSpan.FromDays(1))
                _schedule.Slot2.Value = tod1;
        }

        _schedule.Recompute(DateTime.Now);
        RefreshScheduleMenu();
    }

    // Helpers for the Configure accessory view.

    private static IntPtr CreateLabel(string text, CGRect frame)
    {
        var tf = MsgRect(Msg(Cls("NSTextField"), Sel("alloc")), Sel("initWithFrame:"), frame);
        Msg(tf, Sel("setStringValue:"), NSString(text));
        // Configure as a read-only label: not editable, not selectable, no
        // bezel, no draws-background. mirrors [NSTextField labelWithString:]
        // on macOS 10.12+ but with explicit calls (no convenience class
        // method needed via P/Invoke).
        MsgB(tf, Sel("setEditable:"), 0);
        MsgB(tf, Sel("setSelectable:"), 0);
        MsgB(tf, Sel("setBezeled:"), 0);
        MsgB(tf, Sel("setDrawsBackground:"), 0);
        return tf;
    }

    private static IntPtr CreateTextField(string initialValue, CGRect frame)
    {
        var tf = MsgRect(Msg(Cls("NSTextField"), Sel("alloc")), Sel("initWithFrame:"), frame);
        Msg(tf, Sel("setStringValue:"), NSString(initialValue));
        return tf;
    }

    private static string GetTextFieldString(IntPtr tf)
    {
        var nsstr = Msg(tf, Sel("stringValue"));
        if (nsstr == IntPtr.Zero) return string.Empty;
        var utf8Ptr = Msg(nsstr, Sel("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8Ptr) ?? string.Empty;
    }

    // NSWorkspace session-unlock + power-resume both route here. Re-arm
    // NeverAway iff the last off was auto (not manual).
    [UnmanagedCallersOnly]
    private static void OnWakeOrUnlock(IntPtr self, IntPtr cmd, IntPtr notification)
    {
        if (_isActive || _schedule.Cause != OffCause.Auto) return;
        Toggle(isAuto: false);
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
        IntPtr togglePtr, quitPtr, slot1Ptr, slot2Ptr, cancelPtr, configPtr, autoOffPtr, wakePtr;
        unsafe
        {
            togglePtr  = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnTogglePressed;
            quitPtr    = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnQuitPressed;
            slot1Ptr   = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnSlot1Pressed;
            slot2Ptr   = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnSlot2Pressed;
            cancelPtr  = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnCancelSchedulePressed;
            configPtr  = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnConfigurePressed;
            autoOffPtr = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnAutoOffFired;
            wakePtr    = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnWakeOrUnlock;
        }
        // type encoding "v@:@" = void return, self (id), cmd (SEL), one id arg
        // wake selector takes (id)notification, hence the same v@:@ signature.
        class_addMethod(actionClass, Sel("toggle:"),         togglePtr,  "v@:@");
        class_addMethod(actionClass, Sel("quit:"),           quitPtr,    "v@:@");
        class_addMethod(actionClass, Sel("slot1:"),          slot1Ptr,   "v@:@");
        class_addMethod(actionClass, Sel("slot2:"),          slot2Ptr,   "v@:@");
        class_addMethod(actionClass, Sel("cancelSchedule:"), cancelPtr,  "v@:@");
        class_addMethod(actionClass, Sel("configure:"),      configPtr,  "v@:@");
        class_addMethod(actionClass, Sel("autoOff:"),        autoOffPtr, "v@:@");
        class_addMethod(actionClass, Sel("wakeOrUnlock:"),   wakePtr,    "v@:@");
        objc_registerClassPair(actionClass);
        _actionTarget = Msg(Msg(actionClass, Sel("alloc")), Sel("init"));
        var actionTarget = _actionTarget;

        // NSApplication.sharedApplication, set as Accessory (menu-bar only)
        var app = Msg(Cls("NSApplication"), Sel("sharedApplication"));
        MsgVL(app, Sel("setActivationPolicy:"), 1L); // NSApplicationActivationPolicyAccessory

        // Status item with variable length, icon as title (see IconActive/IconInactive)
        var statusBar = Msg(Cls("NSStatusBar"), Sel("systemStatusBar"));
        _statusItem = MsgD(statusBar, Sel("statusItemWithLength:"), -1.0); // NSVariableStatusItemLength
        _statusButton = Msg(_statusItem, Sel("button"));
        Msg(_statusButton, Sel("setTitle:"), NSString(IconActive));
        Msg(_statusButton, Sel("setToolTip:"), NSString("NeverAway"));

        // Menu: Pause / -- / Slot1 / Slot2 / -- / Cancel scheduled auto-off / -- / Quit
        var menu = Msg(Msg(Cls("NSMenu"), Sel("alloc")), Sel("init"));

        var pauseItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(pauseItem, Sel("setTitle:"), NSString("Pause"));
        Msg(pauseItem, Sel("setAction:"), Sel("toggle:"));
        Msg(pauseItem, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), pauseItem);
        _toggleItem = pauseItem;

        Msg(menu, Sel("addItem:"), Msg(Cls("NSMenuItem"), Sel("separatorItem")));

        _slot1Item = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(_slot1Item, Sel("setAction:"), Sel("slot1:"));
        Msg(_slot1Item, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), _slot1Item);

        _slot2Item = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(_slot2Item, Sel("setAction:"), Sel("slot2:"));
        Msg(_slot2Item, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), _slot2Item);

        var configureItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(configureItem, Sel("setTitle:"), NSString("Configure auto-off..."));
        Msg(configureItem, Sel("setAction:"), Sel("configure:"));
        Msg(configureItem, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), configureItem);

        Msg(menu, Sel("addItem:"), Msg(Cls("NSMenuItem"), Sel("separatorItem")));

        _cancelScheduleItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(_cancelScheduleItem, Sel("setTitle:"), NSString("Cancel scheduled auto-off"));
        Msg(_cancelScheduleItem, Sel("setAction:"), Sel("cancelSchedule:"));
        Msg(_cancelScheduleItem, Sel("setTarget:"), actionTarget);
        Msg(_cancelScheduleItem, Sel("setHidden:"), (IntPtr)1);
        Msg(menu, Sel("addItem:"), _cancelScheduleItem);

        Msg(menu, Sel("addItem:"), Msg(Cls("NSMenuItem"), Sel("separatorItem")));

        var quitItem = Msg(Msg(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
        Msg(quitItem, Sel("setTitle:"), NSString("Quit NeverAway"));
        Msg(quitItem, Sel("setAction:"), Sel("quit:"));
        Msg(quitItem, Sel("setTarget:"), actionTarget);
        Msg(menu, Sel("addItem:"), quitItem);

        Msg(_statusItem, Sel("setMenu:"), menu);

        // Subscribe to two notification streams for auto-on re-arm:
        //
        //   1. NSWorkspaceDidWakeNotification on [NSWorkspace sharedWorkspace]
        //      notificationCenter -- fires when the system wakes from sleep.
        //
        //   2. "com.apple.screenIsUnlocked" on NSDistributedNotificationCenter --
        //      fires when the screen unlocks. NOT exposed via NSWorkspace
        //      (the Workspace SessionDidBecomeActive notification is for
        //      fast-user-switching, NOT screen lock/unlock as one might guess
        //      from the name). The Mac equivalent of Windows'
        //      SessionSwitchReason.SessionUnlock comes through the distributed
        //      notification center.
        var wakeSel = Sel("wakeOrUnlock:");

        var workspace = Msg(Cls("NSWorkspace"), Sel("sharedWorkspace"));
        var wsCenter = Msg(workspace, Sel("notificationCenter"));
        Msg(wsCenter, Sel("addObserver:selector:name:object:"),
            actionTarget, wakeSel,
            NSString("NSWorkspaceDidWakeNotification"), IntPtr.Zero);

        var distCenter = Msg(Cls("NSDistributedNotificationCenter"), Sel("defaultCenter"));
        Msg(distCenter, Sel("addObserver:selector:name:object:"),
            actionTarget, wakeSel,
            NSString("com.apple.screenIsUnlocked"), IntPtr.Zero);

        RefreshScheduleMenu();

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

                    // Schedule check: if the auto-off slot has fired, marshal
                    // the toggle onto the main thread (menu / status-bar
                    // updates aren't thread-safe).
                    if (_schedule.ShouldFire(DateTime.Now) && _actionTarget != IntPtr.Zero)
                    {
                        MsgPerformOnMain(_actionTarget,
                            Sel("performSelectorOnMainThread:withObject:waitUntilDone:"),
                            Sel("autoOff:"), IntPtr.Zero, 0);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        });

        // Run the main runloop -- blocks until terminate:
        Msg(app, Sel("run"));
    }

    // ----- Schedule menu helpers (mirror of Windows TrayApp.RefreshScheduleMenu) -----

    private static void RefreshScheduleMenu()
    {
        if (_slot1Item != IntPtr.Zero)
        {
            Msg(_slot1Item, Sel("setTitle:"), NSString(LabelFor(_schedule.Slot1)));
            MsgVL(_slot1Item, Sel("setState:"), StateValueFor(_schedule.Slot1));
        }
        if (_slot2Item != IntPtr.Zero)
        {
            Msg(_slot2Item, Sel("setTitle:"), NSString(LabelFor(_schedule.Slot2)));
            MsgVL(_slot2Item, Sel("setState:"), StateValueFor(_schedule.Slot2));
        }
        if (_cancelScheduleItem != IntPtr.Zero)
        {
            byte hidden = _schedule.ActiveSlot is null ? (byte)1 : (byte)0;
            MsgB(_cancelScheduleItem, Sel("setHidden:"), hidden);
        }
        UpdateTooltip();
    }

    private static void UpdateTooltip()
    {
        if (_statusButton == IntPtr.Zero) return;
        string tip;
        if (!_isActive)
            tip = "NeverAway is off.";
        else if (_schedule.FireAt is { } t)
            tip = $"NeverAway is on. Auto-off at {t.ToString("h:mm tt", CultureInfo.InvariantCulture)}.";
        else
            tip = "NeverAway is on.";
        Msg(_statusButton, Sel("setToolTip:"), NSString(tip));
    }

    private static string LabelFor(Slot slot)
    {
        var baseLabel = slot.Kind == SlotKind.Duration
            ? FormatDuration(slot.Value)
            : $"Auto-off at {DateTime.Today.Add(slot.Value).ToString("h:mm tt", CultureInfo.InvariantCulture)}";

        return slot.Mode switch
        {
            SlotMode.Once  => $"{baseLabel} (once)",
            SlotMode.Daily => $"Auto-off daily at {DateTime.Today.Add(slot.Value).ToString("h:mm tt", CultureInfo.InvariantCulture)}",
            _              => baseLabel,
        };
    }

    private static string FormatDuration(TimeSpan ts)
    {
        var hours = (int)ts.TotalHours;
        var minutes = ts.Minutes;
        if (hours > 0 && minutes > 0) return $"Auto-off in {hours}h {minutes}m";
        if (hours > 0)                return $"Auto-off in {hours} hour{(hours == 1 ? "" : "s")}";
        return $"Auto-off in {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    private static long StateValueFor(Slot slot) => slot.Mode switch
    {
        SlotMode.Once  => NSControlStateOn,
        SlotMode.Daily => NSControlStateMixed, // visually bolder than a check (mirrors Windows Indeterminate)
        _              => NSControlStateOff,
    };
}
