using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 这些控件不会随正文重建，切换语言时要主动更新文字和辅助功能名称。
    void LocalizeStaticChrome()
    {
        TaskDrawerTitle.Text = Ui("Tasks in progress");
        TaskManagerLabel.Text = Ui("Tasks");
        StatusText.Text = Ui("Ready");
        ProfileButton.Content = Ui("No account added");
        ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("Back"));
        ToolTipService.SetToolTip(PageBackButton, Ui("Back"));
        ToolTipService.SetToolTip(HomeCustomizeButton, Ui("Customize home"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HomeCustomizeButton,
            Ui("Customize home")
        );
        ToolTipService.SetToolTip(TaskDrawerScrim, Ui("Close task panel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            TaskDrawerScrim,
            Ui("Close task panel")
        );
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            TaskProgressRing,
            Ui("Task in progress")
        );
        CancelButton.Content = Ui("Cancel");
    }
}
