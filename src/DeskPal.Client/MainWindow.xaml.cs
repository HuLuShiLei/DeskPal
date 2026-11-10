using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Services;
using DeskPal.Client.ViewModels;
using DeskPal.Client.Views;
using DeskPal.Shared.Api;

namespace DeskPal.Client;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly RoomState _rooms;
    private readonly ApiClient _api;
    private readonly PresenceCoordinator _presence;
    private readonly PersonalRuleStore _personalRules;
    private readonly AppSettings _settings;

    public MainWindow(MainViewModel vm, RoomState rooms, ApiClient api, PresenceCoordinator presence, PersonalRuleStore personalRules, AppSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _rooms = rooms;
        _api = api;
        _presence = presence;
        _personalRules = personalRules;
        _settings = settings;
        DataContext = vm;
        Title = $"DeskPal - {settings.DisplayName}";
        vm.Refresh();
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    public void ShowAndActivate()
    {
        var area = SystemParameters.WorkArea;
        if (!IsVisible && WindowStartupLocation == WindowStartupLocation.Manual && Left == 0 && Top == 0)
        {
            Left = area.Right - Width - 16;
            Top = area.Bottom - Height - 16;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void OnRoomMenuClick(object sender, RoutedEventArgs e)
    {
        var room = _rooms.SelectedRoom;
        var isOwner = room?.OwnerId == _rooms.MyUserId;
        var menu = new ContextMenu { PlacementTarget = RoomMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Item("创建 Room", true, CreateRoomAsync));
        menu.Items.Add(Item("用邀请码加入 Room", true, JoinRoomAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("生成邀请码", room is not null, () => CreateInviteAsync(room!)));
        menu.Items.Add(Item("隐私设置", room is not null, () => EditPrivacyAsync(room!)));
        menu.Items.Add(Item("转让所有者", isOwner && room!.Members.Count > 1, () => TransferAsync(room!)));
        menu.Items.Add(Item("退出 Room", room is not null && !isOwner, () => LeaveAsync(room!)));
        menu.Items.Add(Item("解散 Room", isOwner, () => DissolveAsync(room!)));
        menu.Items.Add(new Separator());
        var topmost = new MenuItem { Header = "窗口置顶", IsCheckable = true, IsChecked = Topmost };
        topmost.Click += (_, _) => Topmost = topmost.IsChecked;
        menu.Items.Add(topmost);
        var autoStart = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = AutoStart.IsEnabled() };
        autoStart.Click += (_, _) => AutoStart.Set(autoStart.IsChecked);
        menu.Items.Add(autoStart);
        menu.IsOpen = true;
    }

    private MenuItem Item(string header, bool enabled, Func<Task> action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await RunAsync(action);
        return item;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            Dialogs.Error(this, ex.Message);
        }
    }

    private async Task CreateRoomAsync()
    {
        var name = Dialogs.Prompt(this, "创建 Room", "Room 名称（1 到 30 个字符）：", "我们的 Room");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var room = await _api.CreateRoomAsync(name);
        _rooms.Select(room.Id);
        await _rooms.RefreshAsync();
    }

    private async Task JoinRoomAsync()
    {
        var code = Dialogs.Prompt(this, "加入 Room", "输入对方发给你的邀请码：");
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var room = await _api.JoinRoomAsync(code);
        _rooms.Select(room.Id);
        await _rooms.RefreshAsync();
    }

    private async Task CreateInviteAsync(RoomDto room)
    {
        var invite = await _api.CreateInviteAsync(room.Id);
        Clipboard.SetText(invite.Code);
        Dialogs.Prompt(this, "邀请码", $"邀请码已复制到剪贴板，{invite.ExpiresAt.LocalDateTime:M 月 d 日 HH:mm} 前有效。重新生成后旧邀请码失效。", invite.Code, readOnly: true);
    }

    private async Task EditPrivacyAsync(RoomDto room)
    {
        if (Dialogs.Privacy(this, room.Name, _rooms.GetPrivacy(room.Id)) is { } profile)
        {
            await _rooms.SavePrivacyAsync(room.Id, profile);
        }
    }

    private async Task TransferAsync(RoomDto room)
    {
        var others = room.Members.Where(m => m.UserId != _rooms.MyUserId).Select(m => (m.DisplayName, m.UserId)).ToList();
        if (Dialogs.Choose(this, "转让所有者", "把所有者身份转让给：", others) is { } target)
        {
            await _api.TransferOwnerAsync(room.Id, target);
            await _rooms.RefreshAsync();
        }
    }

    private async Task LeaveAsync(RoomDto room)
    {
        if (Dialogs.Confirm(this, $"确定退出 Room「{room.Name}」？"))
        {
            await _api.LeaveRoomAsync(room.Id);
            await _rooms.RefreshAsync();
        }
    }

    private async Task DissolveAsync(RoomDto room)
    {
        if (Dialogs.Confirm(this, $"确定解散 Room「{room.Name}」？成员关系和 Room 规则会一起删除，不能恢复。"))
        {
            await _api.DissolveRoomAsync(room.Id);
            await _rooms.RefreshAsync();
        }
    }

    private async void OnPokeClick(object sender, RoutedEventArgs e)
    {
        if (_rooms.SelectedRoom is not { } room || ((Button)sender).Tag is not Guid target)
        {
            return;
        }

        var text = Dialogs.Prompt(this, "戳一下", "想说点什么？可以留空。");
        if (text is null)
        {
            return;
        }

        if (!await _rooms.PokeAsync(room.Id, target, text))
        {
            Dialogs.Error(this, "没有发出去：当前未连接服务器。");
        }
    }

    private async void OnCorrectionClick(object sender, RoutedEventArgs e)
    {
        var target = CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName);
        if (target is null)
        {
            Dialogs.Error(this, "还没有识别到当前使用的应用。切换到要纠正的应用，再从托盘打开本窗口。");
            return;
        }

        var room = _rooms.SelectedRoom;
        var current = _presence.Classification?.Category ?? DeskPal.Shared.Presence.ActivityCategory.Other;
        if (Dialogs.Correction(this, target.DisplayName, current, room?.Name) is not { } choice)
        {
            return;
        }

        var rule = new RuleDto(target.Kind, target.Pattern, choice.Category, target.DisplayName);
        _personalRules.Upsert(rule);
        _presence.ReloadPersonalRules();
        if (choice.ShareToRoom && room is not null)
        {
            await RunAsync(async () =>
            {
                await _api.UpsertRoomRuleAsync(room.Id, new UpsertRoomRuleRequest(rule.Kind, rule.Pattern, rule.Category, rule.Name));
                await _rooms.ReloadRoomRulesAsync();
            });
        }
    }

    private void OnSetManualClick(object sender, RoutedEventArgs e) => _presence.SetManualText(_vm.ManualInput);

    private void OnClearManualClick(object sender, RoutedEventArgs e)
    {
        _vm.ManualInput = "";
        _presence.SetManualText(null);
    }
}