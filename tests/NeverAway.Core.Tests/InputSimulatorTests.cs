using Xunit;

namespace NeverAway.Core.Tests;

public class InputSimulatorTests
{
    // On a supported OS, the factory returns the right concrete type.
    // Early-return on unsupported (e.g. ubuntu CI runner) -- that case
    // is covered by ForCurrentOs_Throws_On_Unsupported below.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public void ForCurrentOs_Returns_PlatformSpecificType()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;
        var sim = InputSimulator.ForCurrentOs();
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsInputSimulator>(sim);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<MacInputSimulator>(sim);
    }

    // On an unsupported OS, the factory throws with a clear message.
    // This is what ubuntu CI exercises -- early-return on win/mac.
    [Fact]
    public void ForCurrentOs_Throws_On_Unsupported()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return;
        Assert.Throws<PlatformNotSupportedException>(() => InputSimulator.ForCurrentOs());
    }

    // Tap() behavior is intentionally NOT unit-tested:
    //   - Windows: keybd_event is a P/Invoke into user32.dll; testing it
    //     would just verify P/Invoke works
    //   - macOS: osascript may need Accessibility permission, awkward in CI
    // Real behavior verification is meatbag-tested:
    //   launch the app, confirm Teams/Slack don't go "Away" after 5+ min idle.
}
