using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Text.Json.Nodes;
using Windows.UI.ViewManagement;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 设备代码只代表授权开始，账号通过 Minecraft 档案验证后才写入列表。
    void Accounts()
    {
        Header("账号管理", "");
        var visibleAccounts = store.Config.Accounts.Where(a => a.Type != "microsoft" || MicrosoftAuth.IsVerifiedMinecraftPlayer(a)).ToList();
        var codePanel = new StackPanel { Spacing = 8 };
        var loginStatus = Text("", 12);
        PageBody.Children.Add(Action("Microsoft 设备代码登录", async (_, _) =>
        {
            codePanel.Children.Clear();
            codePanel.Children.Add(loginStatus);
            loginStatus.Text = Ui("正在申请设备代码…", "Requesting a device code…");
            try
            {
            await Run(async ct =>
            {
                var a = await auth.Login(MicrosoftAuth.ConfiguredClientId, (code, url) =>
                {
                    var copied = DesktopClipboard.TrySetText(code, out var copyError);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                    codePanel.Children.Clear(); codePanel.Children.Add(loginStatus);
                    codePanel.Children.Add(Text("请在浏览器中输入设备代码："));
                    codePanel.Children.Add(new TextBox { Text = code, IsReadOnly = true });
                    void CopyCode()
                    {
                        if (DesktopClipboard.TrySetText(code, out var failure))
                        {
                            loginStatus.Text = Ui("设备代码已复制，请在浏览器中粘贴并提交。", "Device code copied. Paste it in your browser to continue.");
                            return;
                        }
                        AddLog("复制设备代码失败：" + failure);
                        loginStatus.Text = Ui("复制失败，请手动复制上方设备代码。", "Copy failed. Copy the device code above manually.");
                    }
                    void OpenBrowser()
                    {
                        if (!MicrosoftAuth.IsVerificationUrl(url)) return;
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { loginStatus.Text = Ui("浏览器未能自动打开，请点击下方登录链接。", "The browser did not open automatically. Use the sign-in link below."); }
                    }
                    codePanel.Children.Add(Buttons(Action("复制设备代码", (_, _) => CopyCode()), Action("重新打开浏览器", (_, _) => OpenBrowser())));
                    if (MicrosoftAuth.IsVerificationUrl(url)) codePanel.Children.Add(new HyperlinkButton { Content = Ui("打开登录网页", "Open sign-in page"), NavigateUri = new Uri(url) });
                    if (copied) loginStatus.Text = Ui("设备代码已自动复制，请在浏览器中粘贴并提交。", "Device code copied automatically. Paste it in your browser to continue.");
                    else { AddLog("自动复制设备代码失败：" + copyError); loginStatus.Text = Ui("自动复制失败，请手动复制上方设备代码。", "Could not copy automatically. Copy the device code above manually."); }
                    OpenBrowser();
                    });
                }, ct, stage => DispatcherQueue.TryEnqueue(() => loginStatus.Text = Ui(stage)));
                await SaveAccount(a);
            });
            Render();
            }
            catch (OperationCanceledException) { loginStatus.Text = Ui("登录已取消", "Sign-in canceled"); throw; }
            catch { loginStatus.Text = Ui("登录未完成，请查看错误详情；浏览器授权完成后仍需通过 Xbox 和 Minecraft 服务验证。", "Sign-in incomplete. See the error details; browser authorization must still be verified by Xbox and Minecraft services."); throw; }
        }, true)); PageBody.Children.Add(codePanel);
        // 本地至少保留一个已验证的 Microsoft 账号，才显示离线身份入口。
        if (store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer))
        {
            var input = new TextBox { Header = Ui("离线玩家名", "Offline player name"), PlaceholderText = Ui("3–16 位字母、数字或下划线", "3–16 letters, numbers, or underscores"), MaxLength = 16 }; PageBody.Children.Add(input);
            PageBody.Children.Add(Action("添加离线身份", async (_, _) => { await SaveAccount(Account.Offline(input.Text)); Render(); }));
        }
        else PageBody.Children.Add(Text(MicrosoftAuth.OfflineLockedMessage, 12));
        foreach (var account in visibleAccounts)
        {
            var stack = new StackPanel { Spacing = 10 };
            var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            identity.Children.Add(AccountAvatar(account));
            var accountDetails = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            accountDetails.Children.Add(Text(account.Name, 16, true));
            var accountProvider = Text(account.Type == "microsoft" ? "Microsoft" : "离线账号", 11);
            accountProvider.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7"); accountDetails.Children.Add(accountProvider);
            identity.Children.Add(accountDetails);
            stack.Children.Add(identity);
            var allowed = MicrosoftAuth.CanUseAccount(store.Config.Accounts, account);
            if (!allowed) stack.Children.Add(Text(Ui(account.Type == "offline" ? MicrosoftAuth.OfflineLockedMessage : "此账号需要重新通过 Microsoft 登录验证。", "This account must be verified again through Microsoft sign-in."), 12));
            var use = Action(account.Uuid == store.Config.SelectedAccount && allowed ? "当前使用" : "使用此账号", async (_, _) => { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.SelectedAccount = account.Uuid; await store.Save(); Render(); }, account.Uuid == store.Config.SelectedAccount && allowed);
            use.IsEnabled = allowed;
            var remove = Action("移除账号", async (_, _) =>
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = Ui("移除此账号？", "Remove this account?"),
                    Content = store.Config.Settings.LauncherLanguage == "en_us" ? $"Remove {account.Name} and its locally stored credentials from CMCLauncher." : $"将从 CMCLauncher 移除 {account.Name} 及本地保存的凭据。",
                    PrimaryButtonText = Ui("移除", "Remove"),
                    CloseButtonText = Ui("取消", "Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                await ForgetAccount(account);
                await RenderAsync();
            Report(Ui("账号已移除", "Account removed"));
            });
            stack.Children.Add(Buttons(use, remove));
            PageBody.Children.Add(Card(stack));
        }
    }
    async Task SaveAccount(Account account) { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.Accounts.RemoveAll(a => a.Uuid == account.Uuid); store.Config.Accounts.Add(account); store.Config.SelectedAccount = account.Uuid; await store.Save(); }
    // 移除账号后重新检查选中项；失去正版验证条件的离线身份也不能继续选中。
    async Task ForgetAccount(Account account)
    {
        store.Config.Accounts.RemoveAll(a => a.Uuid == account.Uuid);
        if (store.Config.SelectedAccount == account.Uuid || !store.Config.Accounts.Any(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a))) store.Config.SelectedAccount = null;
        await store.Save();
    }
}
