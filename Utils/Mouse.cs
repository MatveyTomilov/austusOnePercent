using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FaustusOnePercent.Utils;

/// <summary>
/// Windows input helper used by the Faustus overlay.
/// Kept inside the plugin instead of depending on another plugin's Utils classes.
/// </summary>
internal static class Mouse
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private const uint LeftButtonDown = 0x0002;
    private const uint LeftButtonUp = 0x0004;

    public static (int X, int Y) GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (point.X, point.Y);
    }

    public static void SetPosition(Vector2 pos)
    {
        // Restoring the cursor should never retry and fight a user moving it.
        SetCursorPos((int)pos.X, (int)pos.Y);
    }

    public static async Task MoveMouse(Vector2 pos)
    {
        int x = (int)pos.X;
        int y = (int)pos.Y;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!SetCursorPos(x, y))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            await Task.Delay(10);

            if (GetCursorPos(out var actual) && actual.X == x && actual.Y == y)
                return;
        }

        throw new OperationCanceledException("Could not move cursor to the selected exchange field.");
    }

    public static async Task LeftDown()
    {
        mouse_event(LeftButtonDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(10);
    }

    public static async Task LeftUp()
    {
        mouse_event(LeftButtonUp, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(10);
    }
}
