using System.Runtime.InteropServices;

namespace NeverAway;

//ref https://stackoverflow.com/questions/16342599/c-sharp-hold-key-in-a-game-application
public class Keyboard
{
    private const int KEY_UP_EVENT = 0x0002; //Key up flag

    public static void KeyUp(byte key)
    {
        keybd_event(key, 0, KEY_UP_EVENT, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
}
