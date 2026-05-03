namespace NeverAway.Core;

// Factory: returns the right simulator for the current OS, or throws
// with a clear message on unsupported platforms.
public static class InputSimulator
{
    public static IInputSimulator ForCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return new WindowsInputSimulator();
        // macOS isn't handled by Core's factory -- NeverAway.Mac has its
        // own CGEvent-based MacInputSimulator instantiated directly. Core
        // only owns the Windows path.
        throw new PlatformNotSupportedException(
            $"NeverAway.Core has no input simulator for {Environment.OSVersion.Platform}. " +
            "On macOS, instantiate NeverAway.Mac.MacInputSimulator directly.");
    }
}
