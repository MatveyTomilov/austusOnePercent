using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FaustusOnePercent.Utils;

/// <summary>
/// Minimal Windows keyboard helper: only Backspace and decimal digits are used.
/// </summary>
internal static class Keyboard
{
    private const uint KeyUpFlag = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private static async Task KeyDown(Keys key)
    {
        keybd_event((byte)key, 0, 0, UIntPtr.Zero);
        await Task.Delay(10);
    }

    private static async Task KeyUp(Keys key)
    {
        keybd_event((byte)key, 0, KeyUpFlag, UIntPtr.Zero);
        await Task.Delay(10);
    }

    public static async Task KeyPress(Keys key)
    {
        await KeyDown(key);
        await KeyUp(key);
    }

    public static async Task Type(string text)
    {
        foreach (char digit in text)
        {
            if (digit < '0' || digit > '9')
                throw new ArgumentException("Only nonnegative integer values can be entered.", nameof(text));
            // Windows digit virtual-key codes equal the ASCII codes 0x30-0x39.
            await KeyPress((Keys)digit);
        }
    }
}
