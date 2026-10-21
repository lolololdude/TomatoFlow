using System.Runtime.InteropServices;

namespace TomatoFlow.Helpers;

public static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public const int GwlExstyle = -20;
    public const int WsExNoactivate = 0x08000000;
    public const int WsExToolwindow = 0x00000080;

    public const int VkLeftButton = 0x01;
    public const byte VkControl = 0x11;
    public const byte VkV = 0x56;
    public const byte VkReturn = 0x0D;
    public const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }

    public static double GetTotalRamGb()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            return status.TotalPhys / 1024d / 1024d / 1024d;
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d;
    }

    public static void SendPaste()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    public static void FocusWindow(IntPtr target)
    {
        if (target == IntPtr.Zero || !IsWindow(target))
        {
            return;
        }

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(target, out _);

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }

        if (targetThread != 0 && targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, true);
        }

        SetForegroundWindow(target);

        if (targetThread != 0 && targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, false);
        }

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public static void MakeNoActivate(IntPtr hWnd)
    {
        var style = GetWindowLongPtr(hWnd, GwlExstyle).ToInt64();
        style |= WsExNoactivate | WsExToolwindow;
        SetWindowLongPtr(hWnd, GwlExstyle, new IntPtr(style));
    }
}
