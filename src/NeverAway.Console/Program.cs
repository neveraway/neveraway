// Console runner. Picks the right input simulator for the current OS,
// taps every 10s. Ctrl+C exits cleanly.
//
// Windows users: prefer the NeverAway.Windows tray app instead — same
// behavior, with a tray-icon toggle so you don't need to keep a console
// window open. This console runner is what Mac/Linux users get.

using NeverAway.Core;

var sim = InputSimulator.ForCurrentOs();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"NeverAway running on {Environment.OSVersion.Platform}. Tap every 10s. Ctrl+C to stop.");

try
{
    while (!cts.IsCancellationRequested)
    {
        try { sim.Tap(); }
        catch (Exception ex)
        {
            // Don't die on a single failure — log + retry next tick.
            // Prevents one bad osascript invocation from stopping the
            // whole process.
            Console.Error.WriteLine($"tap failed: {ex.Message}");
        }
        await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
    }
}
catch (TaskCanceledException) { /* clean shutdown */ }

Console.WriteLine("stopped.");
