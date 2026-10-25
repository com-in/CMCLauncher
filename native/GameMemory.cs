using System.Runtime.InteropServices;

namespace CraftLauncher;

// 为 JVM 堆计算预算，读取系统内存失败时使用固定值。
public static class GameMemory
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static int Calculate(long totalMb, long availableMb)
    {
        // Leave room for Windows, other apps and the JVM's non-heap allocations.
        var budget = Math.Min(totalMb / 2, Math.Max(512, availableMb - 1536));
        // 向下对齐到 256 MB，自动分配最多使用 16 GB。
        return (int)(Math.Clamp(budget, 512, 16384) / 256 * 256);
    }
    public static int Resolve(Settings settings)
    {
        if (!settings.AutoMemory) return Math.Clamp(settings.MemoryMb, 512, 65536);
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status)
            ? Calculate((long)(status.TotalPhysical / 1048576), (long)(status.AvailablePhysical / 1048576))
            : 2048;
    }
}
