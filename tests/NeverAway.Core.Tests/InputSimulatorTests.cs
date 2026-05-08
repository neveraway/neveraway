using Xunit;

namespace NeverAway.Core.Tests;

public class InputSimulatorTests
{
    // On Windows, the factory returns WindowsInputSimulator. On any other
    // OS (including Mac, since the osascript-based MacInputSimulator was
    // dropped along with NeverAway.Console), it throws.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ForCurrentOs_Returns_WindowsSimulator()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sim = InputSimulator.ForCurrentOs();
        Assert.IsType<WindowsInputSimulator>(sim);
    }

    // On non-Windows (ubuntu CI runner, Mac dev machines), the factory
    // throws PlatformNotSupportedException. NeverAway.Mac instantiates
    // its own CGEvent-based simulator directly without going through
    // this factory.
    [Fact]
    public void ForCurrentOs_Throws_On_NonWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() => InputSimulator.ForCurrentOs());
    }

    // Tap() behavior is intentionally NOT unit-tested:
    //   keybd_event is a P/Invoke into user32.dll; testing it would
    //   just verify P/Invoke works. Real behavior verification is
    //   meatbag-tested: launch the app, confirm Teams/Slack don't
    //   go "Away" after 5+ min idle.
}
