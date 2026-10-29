using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 正常退出且没有本次新产生的崩溃文件时，不弹报告对话框。
    async Task HandleGameExit(Instance instance, Process process, DateTimeOffset launchStarted)
    {
        try
        {
            process.Refresh();
            var exitCode = process.ExitCode;
            var crashFile = await Task.Run(() => CrashReport.LatestCrashFile(store, instance));
            if (
                exitCode == 0
                && (
                    crashFile == null
                    || File.GetLastWriteTimeUtc(crashFile) < launchStarted.UtcDateTime
                )
            )
                return;
            while (dialogOpen)
                await Task.Delay(150);
            var logSnapshot = logs.ToArray();
            var report = await Task.Run(() =>
                CrashReport.Build(store, instance, process, logSnapshot)
            );
            // Another dialog may have opened while the report was being read.
            while (dialogOpen)
                await Task.Delay(150);
            AddLog(Ui("The game exited unexpectedly; a crash report summary is ready."));
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(
                new TextBlock
                {
                    Text = UiFormat("The game exited with code {0}.", exitCode),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                }
            );
            content.Children.Add(
                new TextBlock
                {
                    Text = report.Summary,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    MaxLines = 5,
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                    Opacity = .8,
                }
            );
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme
                    ? Microsoft.UI.Xaml.ElementTheme.Light
                    : Microsoft.UI.Xaml.ElementTheme.Dark,
                Title = Ui("The game crashed"),
                Content = content,
                PrimaryButtonText = Ui("Export error report"),
                CloseButtonText = Ui("context.crashreport.close"),
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            };
            dialogOpen = true;
            try
            {
                if (
                    await dialog.ShowAsync()
                    == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary
                )
                    await ExportCrashReport(report);
            }
            finally
            {
                dialogOpen = false;
            }
        }
        catch (Exception ex)
        {
            AddLog("Crash report failed: " + ex.Message);
        }
    }

    async Task ExportCrashReport(CrashReport report)
    {
        var picker = new FileSavePicker { SuggestedFileName = report.FileName };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add(Ui("Error report"), [".txt"]);
        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return;
        await File.WriteAllTextAsync(file.Path, report.Text, Encoding.UTF8);
        Report(Ui("Error report exported."));
    }
}

public sealed record CrashReport(string FileName, string Summary, string Text)
{
    public static string? LatestCrashFile(Store store, Instance instance)
    {
        var directory = Path.Combine(store.GameDirectory(instance), "crash-reports");
        return Directory.Exists(directory)
            ? Directory
                .GetFiles(directory, "*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
    }

    // 汇总游戏崩溃文件和日志片段，某份日志读不到也要能导出其他诊断信息。
    public static CrashReport Build(
        Store store,
        Instance instance,
        Process process,
        IEnumerable<string> launcherLog
    )
    {
        var gameDir = store.GameDirectory(instance);
        var crash = LatestCrashFile(store, instance);
        var latest = Path.Combine(gameDir, "logs", "latest.log");
        var logText = ReadTail(latest, 120_000);
        var crashText = crash == null ? "(no crash report file found)" : ReadTail(crash, 200_000);
        var summary = crash != null ? Path.GetFileName(crash) : FindSummary(logText);
        var text = new StringBuilder()
            .AppendLine("CMCLauncher game error report")
            .AppendLine("Generated: " + DateTimeOffset.Now.ToString("O"))
            .AppendLine("Instance: " + instance.Name)
            .AppendLine("Minecraft: " + instance.GameVersion)
            .AppendLine(
                "Loader: "
                    + instance.Loader
                    + (
                        string.IsNullOrWhiteSpace(instance.LoaderVersion)
                            ? ""
                            : " " + instance.LoaderVersion
                    )
            )
            .AppendLine("Exit code: " + SafeExitCode(process))
            .AppendLine()
            .AppendLine("Crash report:")
            .AppendLine(crashText)
            .AppendLine()
            .AppendLine("latest.log (tail):")
            .AppendLine(logText)
            .AppendLine()
            .AppendLine("Launcher log (tail):")
            .AppendLine(RuntimeLog.ReadTail())
            .ToString();
        return new CrashReport(
            "CMCLauncher-error-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
            summary,
            text
        );
    }

    static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    static string ReadTail(string path, int max)
    {
        try
        {
            if (!File.Exists(path))
                return "(file not found)";
            return LogTail.Read(path, max);
        }
        catch (Exception ex)
        {
            return "(cannot read log: " + ex.Message + ")";
        }
    }

    static string FindSummary(string log) =>
        log.Split('\n')
            .Reverse()
            .FirstOrDefault(line =>
                line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            )
            ?.Trim()
        ?? "No crash report file was found; see latest.log below.";
}
