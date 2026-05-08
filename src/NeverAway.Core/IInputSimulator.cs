namespace NeverAway.Core;

// Send a single key event that registers as user input to the OS without
// actually doing anything visible. v2 used user32.dll keybd_event with
// F24 (no modern keyboard has F24, so it's safe to send). The Mac /
// Linux equivalents pick keys with the same property: present in the
// keymap but practically never bound to anything (F15, F19).
public interface IInputSimulator
{
    void Tap();
}
