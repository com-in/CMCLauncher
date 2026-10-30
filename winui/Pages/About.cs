using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void AboutPage()
    {
        Header("About", "Version and build details");
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        Text("CMCLauncher", 20, true),
                        Text("A fast, modern Minecraft launcher.", 13),
                        new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                Text(Labeled("Version", LauncherBuildInfo.Version), 13),
                                Text(
                                    Labeled(
                                        "Is release",
                                        LauncherBuildInfo.IsRelease
                                            ? Ui("Release build")
                                            : Ui("Development build")
                                    ),
                                    13
                                ),
                                Text(Labeled("Build time", LauncherBuildInfo.BuildTime), 13),
                                Text(Labeled("Commit", LauncherBuildInfo.GitCommit), 13),
                            },
                        },
                        new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                Text("Developers", 18, true),
                                Text("Ctoy · Fuzhu", 13),
                            },
                        },
                        Text(
                            "This page's information is generated at build time and is for reference only.",
                            11
                        ),
                    },
                }
            )
        );
    }

    string Labeled(string label, string value) => Ui(label) + "  " + value;
}