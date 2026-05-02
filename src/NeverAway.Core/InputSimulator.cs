namespace NeverAway.Core;

// Factory: returns the right simulator for the current OS, or throws
// with a clear message on unsupported platforms.
public static class InputSimulator
{
    public static IInputSimulator ForCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return new WindowsInputSimulator();
        if (OperatingSystem.IsMacOS())   return new MacInputSimulator();
        throw new PlatformNotSupportedException(
            $"NeverAway has no input simulator for {Environment.OSVersion.Platform}.");
    }
}
