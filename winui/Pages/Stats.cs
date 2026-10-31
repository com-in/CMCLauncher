using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
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
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    static ulong FileTimeTick(FileTime t) => ((ulong)t.High << 32) | t.Low;

    ulong sysLastIdle, sysLastKernel, sysLastUser;
    bool sysFirst;
    DispatcherTimer? statsTimer;
    readonly SemaphoreSlim gpuSampleLock = new(1, 1);
    double lastGpuUsage = -1;

    void StatsPage()
    {
        Header(Ui("System status"), Ui("Live CPU, memory and graphics usage"));
        var cpuValue = Text("", 18, true);
        var memValue = Text("", 14);
        var processValue = Text("", 14);
        var gpuValue = Text("", 14);
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        StatRow(Ui("CPU usage"), cpuValue),
                        StatRow(Ui("Memory usage"), memValue),
                        StatRow(Ui("Launcher process"), processValue),
                        StatRow(Ui("Graphics"), gpuValue),
                    },
                }
            )
        );

        if (statsTimer != null)
            statsTimer.Stop();
        statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        sysFirst = true;
        lastGpuUsage = -1;
        statsTimer.Tick += async (_, _) =>
        {
            GetSystemTimes(out var idleNow, out var kernelNow, out var userNow);
            var idle = FileTimeTick(idleNow);
            var kernel = FileTimeTick(kernelNow);
            var user = FileTimeTick(userNow);
            double cpu = -1;
            if (!sysFirst)
            {
                var di = idle - sysLastIdle;
                var dk = kernel - sysLastKernel;
                var du = user - sysLastUser;
                var total = dk + du;
                var busy = total > di ? total - di : 0UL;
                cpu = total > 0 ? 100.0 * busy / total : 0;
            }
            sysLastIdle = idle;
            sysLastKernel = kernel;
            sysLastUser = user;
            sysFirst = false;

            var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            GlobalMemoryStatusEx(ref ms);
            var used = ms.TotalPhys - ms.AvailPhys;
            var memPct = ms.TotalPhys > 0 ? 100.0 * used / ms.TotalPhys : 0;

            using var process = Process.GetCurrentProcess();
            var workingMB = process.WorkingSet64 / 1048576.0;
            var privateMB = process.PrivateMemorySize64 / 1048576.0;

            cpuValue.Text = cpu < 0 ? "" : cpu.ToString("0.0") + "%";
            memValue.Text =
                (used / 1073741824.0).ToString("0.00")
                + " GB / "
                + (ms.TotalPhys / 1073741824.0).ToString("0.00")
                + " GB   ("
                + memPct.ToString("0.0")
                + "%)";
            processValue.Text = workingMB.ToString("0")
                + " MB working set · "
                + privateMB.ToString("0")
                + " MB private";
            var gpuPc = await SampleGpuUsageAsync();
            gpuValue.Text = gpuPc < 0 ? Ui("Not available") : gpuPc.ToString("0") + "%";
        };
        statsTimer.Start();
    }

    StackPanel StatRow(string label, TextBlock value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(Text(label, 12, true));
        row.Children.Add(value);
        return row;
    }

    async Task<double> SampleGpuUsageAsync()
    {
        if (!await gpuSampleLock.WaitAsync(0))
            return lastGpuUsage;
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments =
                        "path Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine get UtilizationPercentage",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            if (process == null)
                return -1;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
                return -1;
            double sum = 0;
            var count = 0;
            foreach (var line in output.Split('\n'))
            {
                if (int.TryParse(line.Trim(), out var value))
                {
                    sum += value;
                    count++;
                }
            }
            lastGpuUsage = count > 0 ? sum / count : -2;
            return lastGpuUsage;
        }
        catch
        {
            return lastGpuUsage;
        }
        finally
        {
            gpuSampleLock.Release();
        }
    }
}