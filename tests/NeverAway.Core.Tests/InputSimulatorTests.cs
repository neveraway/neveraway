using Xunit;

namespace NeverAway.Core.Tests;

public class InputSimulatorTests
{
    // Factory should always return SOMETHING for the OS this test is
    // running on. We can't assert which concrete type without
    // platform-conditional asserts; just confirm it's a non-null
    // IInputSimulator.
    [Fact]
    public void ForCurrentOs_Returns_NonNull_Simulator()
    {
        var sim = InputSimulator.ForCurrentOs();
        Assert.NotNull(sim);
        Assert.IsAssignableFrom<IInputSimulator>(sim);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void ForCurrentOs_Returns_PlatformSpecificType()
    {
        var sim = InputSimulator.ForCurrentOs();
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsInputSimulator>(sim);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<MacInputSimulator>(sim);
        else if (OperatingSystem.IsLinux()) Assert.IsType<LinuxInputSimulator>(sim);
    }

    // Tap() behavior is intentionally NOT unit-tested:
    //   - Windows: keybd_event is a P/Invoke into user32.dll; testing it
    //     would just verify P/Invoke works
    //   - macOS: osascript may need Accessibility permission, awkward in CI
    //   - Linux: xdotool may not be installed in CI
    // Real behavior verification is meatbag-tested:
    //   launch the app, confirm Teams/Slack don't go "Away" after 5+ min idle.
}
