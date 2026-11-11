using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Services;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.SignalR.Client;

namespace DeskPal.Client.ViewModels;

public sealed class MemberCard
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string Headline { get; init; }
    public string? App { get; init; }
    public string? Detail { get; init; }
    public required string Meta { get; init; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly RoomState _rooms;
    private readonly PresenceCoordinator _presence;
    private readonly DispatcherTimer _timer;
    private HubConnectionState _connection = HubConnectionState.Disconnected;
    private bool _refreshQueued;
    private bool _syncingSelection;
    private RoomDto? _selectedRoom;
    private string _connectionText = "正在连接服务器…";
    private PresenceView _myView = new("正在识别…", null, null);
    private string _mySource = "";
    private string _myVisibleText = "";
    private string _manualInput = "";
    private Visibility _emptyHintVisibility = Visibility.Collapsed;
    private string _emptyHint = "";

    public MainViewModel(RoomState rooms, PresenceCoordinator presence, HubService hub)
    {
        _rooms = rooms;
        _presence = presence;
        _manualInput = presence.ManualText ?? "";
        rooms.Changed += Schedule;
        presence.LocalChanged += Schedule;
        hub.StateChanged += state =>
        {
            _connection = state;
            Schedule();
        };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Refresh(), Application.Current.Dispatcher);
        _timer.Start();
    }

    public ObservableCollection<RoomDto> Rooms { get; } = new();

    public ObservableCollection<MemberCard> Members { get; } = new();

    public RoomDto? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            if (SetProperty(ref _selectedRoom, value) && !_syncingSelection && value is not null)
            {
                _rooms.Select(value.Id);
            }
        }
    }

    public string ConnectionText { get => _connectionText; private set => SetProperty(ref _connectionText, value); }

    public PresenceView MyView { get => _myView; private set => SetProperty(ref _myView, value); }

    public string MySource { get => _mySource; private set => SetProperty(ref _mySource, value); }

    public string MyVisibleText { get => _myVisibleText; private set => SetProperty(ref _myVisibleText, value); }

    public string ManualInput { get => _manualInput; set => SetProperty(ref _manualInput, value); }

    public string EmptyHint { get => _emptyHint; private set => SetProperty(ref _emptyHint, value); }

    public Visibility EmptyHintVisibility { get => _emptyHintVisibility; private set => SetProperty(ref _emptyHintVisibility, value); }

    public bool IsPaused
    {
        get => _presence.Paused;
        set
        {
            if (value != _presence.Paused)
            {
                _presence.SetPaused(value);
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<string> ManualPresets { get; } = ["别打扰", "午休中", "开会中", "有空，来聊", "在路上"];

    public void Schedule()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    public void Refresh()
    {
        SyncRooms();
        RefreshMembers();
        RefreshMyStatus();
        ConnectionText = _connection switch
        {
            HubConnectionState.Connected => "已连接",
            HubConnectionState.Connecting => "正在连接服务器…",
            HubConnectionState.Reconnecting => "连接中断，正在重连…",
            _ => "未连接服务器，稍后自动重试",
        };
        OnPropertyChanged(nameof(IsPaused));
    }

    private void SyncRooms()
    {
        var rooms = _rooms.Rooms;
        if (!rooms.Select(r => (r.Id, r.Name, r.Members.Count, r.OwnerId)).SequenceEqual(Rooms.Select(r => (r.Id, r.Name, r.Members.Count, r.OwnerId))))
        {
            _syncingSelection = true;
            Rooms.Clear();
            foreach (var room in rooms)
            {
                Rooms.Add(room);
            }

            _syncingSelection = false;
        }

        var selected = _rooms.SelectedRoom;
        _syncingSelection = true;
        SelectedRoom = Rooms.FirstOrDefault(r => r.Id == selected?.Id);
        _syncingSelection = false;
    }

    private void RefreshMembers()
    {
        Members.Clear();
        var room = SelectedRoom;
        if (room is null)
        {
            EmptyHint = "还没有 Room。点右上角的“Room”菜单，创建 Room 或者用邀请码加入。";
            EmptyHintVisibility = Visibility.Visible;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var member in room.Members.Where(m => m.UserId != _rooms.MyUserId))
        {
            var snapshot = _rooms.GetPresence(room.Id, member.UserId);
            var view = snapshot is null ? new PresenceView("离线", null, null) : PresenceFormatter.Format(snapshot);
            Members.Add(new MemberCard
            {
                UserId = member.UserId,
                Name = member.Role == RoomRole.Owner ? $"{member.DisplayName}（所有者）" : member.DisplayName,
                Headline = view.Headline,
                App = view.App,
                Detail = view.Detail,
                Meta = snapshot is null ? "还没有收到状态" : $"已持续 {PresenceFormatter.Duration(snapshot.Since, now)}",
            });
        }

        EmptyHint = "Room 里还没有其他成员。点“Room”菜单里的“生成邀请码”，把邀请码发给对方。";
        EmptyHintVisibility = Members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMyStatus()
    {
        var local = _presence.Local;
        if (local is null)
        {
            return;
        }

        var full = new PresenceSnapshot
        {
            State = local.State,
            Category = local.Category,
            AppDisplayName = local.AppDisplayName,
            Detail = local.Detail,
            CustomText = local.CustomText,
        };
        var source = _presence.Classification?.Layer switch
        {
            RuleLayer.Personal => "个人规则",
            RuleLayer.Room => "Room 规则",
            RuleLayer.Builtin => "内置规则",
            RuleLayer.Heuristic => "推测",
            _ => "未识别",
        };
        var pending = _presence.Committed is { } c && c.Presence.Key != local.Key ? "稳定后同步" : null;
        MyView = PresenceFormatter.Format(full);
        MySource = string.Join("，", new[] { local.Category == ActivityCategory.None ? null : source, pending }.Where(part => part is not null));

        if (SelectedRoom is { } room && _presence.Committed is { } committed)
        {
            var visible = PrivacyFilter.Apply(committed, _rooms.GetPrivacy(room.Id));
            MyVisibleText = $"该 Room 成员看到：{PresenceFormatter.OneLine(PresenceFormatter.Format(visible))}";
        }
        else
        {
            MyVisibleText = "";
        }
    }
}