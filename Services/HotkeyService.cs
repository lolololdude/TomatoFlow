using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x0D1D;
    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyService(IntPtr windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle)
                  ?? throw new InvalidOperationException("The window handle is not ready.");
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public HotkeyGesture? Current { get; private set; }

    public bool Register(HotkeyGesture gesture, out string error)
    {
        error = "";
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        if (gesture.Modifiers == HotkeyModifiers.None && !IsFunctionKey(gesture.VirtualKey))
        {
            error = "Add Ctrl, Alt, Shift, or Win to the shortcut.";
            return false;
        }

        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, (uint)gesture.Modifiers, (uint)gesture.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == 1409
                ? "That shortcut is already used by Windows or another app."
                : new Win32Exception(code).Message;
            Current = null;
            return false;
        }

        _registered = true;
        Current = gesture;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static bool IsFunctionKey(int virtualKey)
    {
        return virtualKey is >= 0x70 and <= 0x87;
    }

    public static bool IsTypingKey(Key key)
    {
        return key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9
            or Key.Space or Key.OemPlus or Key.OemMinus or Key.OemComma or Key.OemPeriod
            or Key.OemQuestion or Key.OemTilde or Key.OemOpenBrackets or Key.OemCloseBrackets
            or Key.OemPipe or Key.OemQuotes or Key.OemSemicolon;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
