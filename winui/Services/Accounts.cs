using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 设备代码只代表授权开始，账号通过 Minecraft 档案验证后才写入列表。
    void Accounts()
    {
        Header("账号管理", "选择用于启动游戏的身份，或添加新的账号。");
        var visibleAccounts = store
            .Config.Accounts.Where(a =>
                a.Type != "microsoft" || MicrosoftAuth.IsVerifiedMinecraftPlayer(a)
            )
            .ToList();
        var accountList = new StackPanel { Spacing = 12 };
        accountList.Children.Add(Text("已保存的账号", 18, true));
        PageBody.Children.Add(accountList);
        if (visibleAccounts.Count == 0)
            accountList.Children.Add(Card(Text("还没有账号，请在下方登录 Microsoft 账号。", 13)));

        var addAccount = new StackPanel { Spacing = 14 };
        addAccount.Children.Add(Text("添加账号", 18, true));
        var microsoft = new StackPanel { Spacing = 10 };
        microsoft.Children.Add(Text("Microsoft", 16, true));
        microsoft.Children.Add(Text("使用拥有 Minecraft 的 Microsoft 账号登录。", 12));
        addAccount.Children.Add(Card(microsoft));
        PageBody.Children.Add(addAccount);
        var codePanel = new StackPanel { Spacing = 8 };
        var loginStatus = Text("", 12);
        microsoft.Children.Add(
            Action(
                "Microsoft 设备代码登录",
                async (_, _) =>
                {
                    codePanel.Children.Clear();
                    codePanel.Children.Add(loginStatus);
                    loginStatus.Text = Ui("Requesting a device code…");
                    try
                    {
                        await Run(async ct =>
                        {
                            var a = await auth.Login(
                                MicrosoftAuth.ConfiguredClientId,
                                (code, url) =>
                                {
                                    var copied = DesktopClipboard.TrySetText(
                                        code,
                                        out var copyError
                                    );
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        codePanel.Children.Clear();
                                        codePanel.Children.Add(loginStatus);
                                        codePanel.Children.Add(Text("请在浏览器中输入设备代码："));
                                        codePanel.Children.Add(
                                            new TextBox { Text = code, IsReadOnly = true }
                                        );
                                        void CopyCode()
                                        {
                                            if (DesktopClipboard.TrySetText(code, out var failure))
                                            {
                                                loginStatus.Text = Ui(
                                                    "Device code copied. Paste it in your browser to continue."
                                                );
                                                return;
                                            }
                                            AddLog("复制设备代码失败：" + failure);
                                            loginStatus.Text = Ui(
                                                "Copy failed. Copy the device code above manually."
                                            );
                                        }
                                        void OpenBrowser()
                                        {
                                            if (!MicrosoftAuth.IsVerificationUrl(url))
                                                return;
                                            try
                                            {
                                                Process.Start(
                                                    new ProcessStartInfo(url)
                                                    {
                                                        UseShellExecute = true,
                                                    }
                                                );
                                            }
                                            catch
                                            {
                                                loginStatus.Text = Ui(
                                                    "The browser did not open automatically. Use the sign-in link below."
                                                );
                                            }
                                        }
                                        codePanel.Children.Add(
                                            Buttons(
                                                Action("复制设备代码", (_, _) => CopyCode()),
                                                Action("重新打开浏览器", (_, _) => OpenBrowser())
                                            )
                                        );
                                        if (MicrosoftAuth.IsVerificationUrl(url))
                                            codePanel.Children.Add(
                                                new HyperlinkButton
                                                {
                                                    Content = Ui("Open sign-in page"),
                                                    NavigateUri = new Uri(url),
                                                }
                                            );
                                        if (copied)
                                            loginStatus.Text = Ui(
                                                "Device code copied automatically. Paste it in your browser to continue."
                                            );
                                        else
                                        {
                                            AddLog("自动复制设备代码失败：" + copyError);
                                            loginStatus.Text = Ui(
                                                "Could not copy automatically. Copy the device code above manually."
                                            );
                                        }
                                        OpenBrowser();
                                    });
                                },
                                ct,
                                stage =>
                                    DispatcherQueue.TryEnqueue(() => loginStatus.Text = Ui(stage))
                            );
                            await SaveAccount(a);
                        });
                        Render();
                    }
                    catch (OperationCanceledException)
                    {
                        loginStatus.Text = Ui("Sign-in canceled");
                        throw;
                    }
                    catch
                    {
                        loginStatus.Text = Ui(
                            "Sign-in incomplete. See the error details; browser authorization must still be verified by Xbox and Minecraft services."
                        );
                        throw;
                    }
                },
                true
            )
        );
        microsoft.Children.Add(codePanel);
        var otherAccounts = new Grid { ColumnSpacing = 14 };
        otherAccounts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        otherAccounts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addAccount.Children.Add(otherAccounts);
        var external = new StackPanel { Spacing = 12 };
        var externalCard = Card(external);
        externalCard.VerticalAlignment = VerticalAlignment.Top;
        otherAccounts.Children.Add(externalCard);
        ExternalAccountForm(external);
        var offline = new StackPanel { Spacing = 10 };
        offline.Children.Add(Text("离线身份", 16, true));
        var offlineCard = Card(offline);
        offlineCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(offlineCard, 1);
        otherAccounts.Children.Add(offlineCard);
        // 本地至少保留一个已验证的 Microsoft 账号，才显示离线身份入口。
        if (store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer))
        {
            var input = new TextBox
            {
                Header = Ui("Offline player name"),
                PlaceholderText = Ui("3–16 letters, numbers, or underscores"),
                MaxLength = 16,
            };
            offline.Children.Add(input);
            offline.Children.Add(
                Action(
                    "添加离线身份",
                    async (_, _) =>
                    {
                        await SaveAccount(Account.Offline(input.Text));
                        Render();
                    }
                )
            );
        }
        else
            offline.Children.Add(Text(MicrosoftAuth.OfflineLockedMessage, 12));
        foreach (var account in visibleAccounts)
        {
            var stack = new StackPanel { Spacing = 10 };
            var accountDetails = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
            };
            accountDetails.Children.Add(Text(account.Name, 16, true));
            var accountProvider = Text(
                account.Type == "microsoft" ? "Microsoft"
                    : account.Type == "yggdrasil" ? account.AuthServer
                    : "离线账号",
                11
            );
            accountProvider.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7");
            accountDetails.Children.Add(accountProvider);
            var identityGrid = new Grid { ColumnSpacing = 12 };
            identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identityGrid.Children.Add(AccountAvatar(account));
            Grid.SetColumn(accountDetails, 1);
            identityGrid.Children.Add(accountDetails);
            stack.Children.Add(identityGrid);
            var allowed = MicrosoftAuth.CanUseAccount(store.Config.Accounts, account);
            if (!allowed)
                stack.Children.Add(
                    Text(
                        Ui(
                            account.Type == "yggdrasil" ? MicrosoftAuth.ExternalLockedMessage
                            : account.Type == "offline"
                                ? "context.accounts.this_account_must_be_verified_again_through_microsoft_sign_in"
                            : "此账号需要重新通过 Microsoft 登录验证。"
                        ),
                        12
                    )
                );
            var use = Action(
                account.Key == store.Config.SelectedAccount && allowed ? "当前使用" : "使用此账号",
                async (_, _) =>
                {
                    MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
                    store.Config.SelectedAccount = account.Key;
                    await store.Save();
                    Render();
                },
                account.Key == store.Config.SelectedAccount && allowed
            );
            use.IsEnabled = allowed;
            var remove = Action(
                "移除账号",
                async (sender, _) =>
                {
                    if (
                        !await ConfirmPop(
                            (FrameworkElement)sender,
                            Ui("Remove this account?"),
                            account.Name,
                            Ui("Remove")
                        )
                    )
                        return;
                    await ForgetAccount(account);
                    await RenderAsync();
                    Report(Ui("Account removed"));
                    ShowToast(Ui("Account removed"));
                }
            );
            var actions = Buttons(use, remove);
            actions.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.Children.Add(stack);
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            row.SizeChanged += (_, args) =>
            {
                var narrow = args.NewSize.Width < 620;
                Grid.SetColumnSpan(stack, narrow ? 2 : 1);
                Grid.SetRow(actions, narrow ? 1 : 0);
                Grid.SetColumn(actions, narrow ? 0 : 1);
                Grid.SetColumnSpan(actions, narrow ? 2 : 1);
                actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                actions.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
            };
            accountList.Children.Add(Card(row));
        }
    }

    void ExternalAccountForm(StackPanel container)
    {
        var unlocked = store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer);
        if (!unlocked)
            container.Children.Add(Text(MicrosoftAuth.ExternalLockedMessage, 12));
        container.Children.Add(Text("External sign-in (authlib-injector)", 16, true));
        container.Children.Add(new ContentControl
        {
            Content = ExternalAccountFields(),
            Tag = "external-account-form",
            IsEnabled = unlocked,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        });
    }

    StackPanel ExternalAccountFields()
    {
        var form = new StackPanel { Spacing = 12 };
        var server = new TextBox
        {
            Header = Ui("Authentication server"),
            PlaceholderText = "https://example.com/api/yggdrasil/",
        };
        var login = new TextBox
        {
            Header = Ui("External account"),
            PlaceholderText = Ui("Username or email"),
        };
        var password = new PasswordBox { Header = Ui("Password") };
        var serverRow = new Grid { ColumnSpacing = 8 };
        serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        serverRow.Children.Add(server);
        var littleSkin = Action("LittleSkin", (_, _) => server.Text = "https://littleskin.cn/api/yggdrasil");
        littleSkin.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(littleSkin, 1);
        serverRow.Children.Add(littleSkin);
        form.Children.Add(serverRow);
        form.Children.Add(login);
        form.Children.Add(password);
        form.Children.Add(
            Action(
                "Sign in to external account",
                async (_, _) =>
                {
                    MicrosoftAuth.EnsureExternalAllowed(store.Config.Accounts);
                    try
                    {
                        await Run(async ct =>
                        {
                            using var external = new ExternalAuth(store.Config.Settings);
                            var account = await external.Login(
                                store.Config.Accounts,
                                server.Text,
                                login.Text,
                                password.Password,
                                async profiles =>
                                {
                                    var picker = new ComboBox
                                    {
                                        ItemsSource = profiles,
                                        SelectedIndex = 0,
                                        HorizontalAlignment = HorizontalAlignment.Stretch,
                                    };
                                    var dialog = new ContentDialog
                                    {
                                        XamlRoot = Content.XamlRoot,
                                        Title = Ui("Select a player profile"),
                                        Content = picker,
                                        PrimaryButtonText = Ui("Continue"),
                                        CloseButtonText = Ui("Cancel"),
                                        DefaultButton = ContentDialogButton.Primary,
                                    };
                                    return await dialog.ShowAsync() == ContentDialogResult.Primary
                                        ? picker.SelectedItem as ExternalProfile
                                        : null;
                                },
                                ct
                            );
                            await SaveAccount(account);
                        });
                        await RenderAsync();
                    }
                    finally
                    {
                        password.Password = "";
                    }
                },
                true
            )
        );
        return form;
    }

    async Task SaveAccount(Account account)
    {
        MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
        store.Config.Accounts.RemoveAll(a => a.Key == account.Key);
        store.Config.Accounts.Add(account);
        store.Config.SelectedAccount = account.Key;
        await store.Save();
    }

    // 移除账号后重新检查选中项；失去正版验证条件的离线身份也不能继续选中。
    async Task ForgetAccount(Account account)
    {
        store.Config.Accounts.RemoveAll(a => a.Key == account.Key);
        if (
            store.Config.SelectedAccount == account.Key
            || !store.Config.Accounts.Any(a =>
                a.Key == store.Config.SelectedAccount
                && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a)
            )
        )
            store.Config.SelectedAccount = null;
        await store.Save();
    }
}
