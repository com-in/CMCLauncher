using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CMCLauncher.WinUI;

// 桌面版用 Win32 剪贴板写 Unicode 文本，错误交给调用方展示。
internal static class DesktopClipboard
{
    const uint GlobalMoveable = 0x0002;
    const uint UnicodeText = 13;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalFree(IntPtr memory);

    public static bool TrySetText(string text, out string? error)
    {
        error = null;
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(GlobalMoveable, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
        var opened = false;
        try
        {
            var locked = GlobalLock(memory);
            if (locked == IntPtr.Zero) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
            try { Marshal.Copy(bytes, 0, locked, bytes.Length); }
            finally { GlobalUnlock(memory); }

            // 剪贴板可能暂时被其他进程占用，做短暂重试后再报告失败。
            for (var attempt = 0; attempt < 20 && !opened; attempt++)
            {
                opened = OpenClipboard(IntPtr.Zero);
                if (!opened) Thread.Sleep(25);
            }
            if (!opened) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
            if (!EmptyClipboard()) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
            if (SetClipboardData(UnicodeText, memory) == IntPtr.Zero) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
            // SetClipboardData 成功后内存归系统所有，finally 不能再释放。
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (opened) CloseClipboard();
            if (memory != IntPtr.Zero) GlobalFree(memory);
        }
    }
}
