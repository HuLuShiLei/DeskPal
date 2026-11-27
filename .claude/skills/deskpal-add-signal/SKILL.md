---
name: deskpal-add-signal
description: 给 DeskPal 客户端新增一种活动信号或采集器（例如摄像头占用、键盘输入次数、电源状态），从 ActivitySignal、插件能力、官方插件里的采集器、ActivitySnapshot、PresenceResolver 一直到测试。用户要让状态判断多参考一种系统信息，或者游戏模块需要新的输入数据时使用。
---

# 新增活动信号

先确认这个信号用普通用户权限就能拿到，并且不会读到内容（见根目录 `AGENTS.md` 硬约束的采集器一条）。做不到就停下来，和用户商量替代方案。

## 1. 定义信号（Client.Core）

`src/DeskPal.Client.Core/Pipeline/ActivitySignal.cs`：

```csharp
public sealed record CameraSignal(bool InUse, string? AppName) : ActivitySignal;
```

只放事实，不放判断结果。键盘输入只能是**次数**，不能是按了什么键。

## 2. 让插件能提供它

所有信号都经过插件宿主（`ProviderHost`）：每种信号属于一项能力，按插件列表决定用哪个插件的。**不登记能力的信号，宿主会直接报错。**

- `src/DeskPal.Client.Core/Providers/ProviderCapability.cs`：枚举加一项；`ProviderCapabilities` 里补 `All`、`Name`、`Of`（信号 → 能力）、`Cleared`（没有插件提供时发什么）。
- 用户插件也要能提供的话，在 `ProviderProtocol` 里加对应的消息类型（只加，不改旧的），`ExternalProvider` 里把消息转成信号；同步改 skill `deskpal-write-provider` 的协议表、`scripts/check_provider.py` 和 `docs/provider-guide.md`。
- 前端：`bridge.ts` 的 `PluginCapability`、`plugins.cap.<名字>` 中英文案，设置页的插件标签会显示它。
- 测试：`ProviderProtocolTests`、`CapabilityRouterTests` 各加一条。

## 3. 并入快照

`ActivitySnapshot` 加属性并在 `Apply` 里加一个分支：

```csharp
public CameraSignal Camera { get; init; } = new(false, null);
...
CameraSignal c => this with { Camera = c },
```

只给游戏模块用的高频信号（例如输入次数）**不要并入快照**。游戏模块自己 `bus.Subscribe()`（订阅 `ActivityBus`，拿到的是宿主采用后的信号），否则每次输入都会让状态重新计算一遍。

## 4. 采集器（各平台官方插件）

`src/DeskPal.Client/Collectors/CameraCollector.cs`，参照 `IdleCollector`：

- 继承 `BackgroundService`，构造函数收 `SignalSink bus`（**不是** `ActivityBus`）和 `ILogger<T>`。官方插件每次启动都新建采集器并传入这次的 `SignalSink`，关掉插件时停止它。
- 用 `PeriodicTimer` 轮询，只在值变化时 `bus.Publish(...)`。
- 系统调用包在 try/catch 里，失败只记 Debug 日志。
- 需要 P/Invoke 的，声明加在 `Native/NativeMethods.cs`。

把类型加进官方插件的部件列表，并在能力集合里加上新能力：Windows `App.xaml.cs` 和 macOS `AppDelegate.cs` 的 `AddOfficialProvider(...)`，Linux `Providers/LinuxOfficialProvider.cs` 的构造函数。不要再用 `AddHostedService` 单独注册采集器。某个平台做不到时，那个平台的官方插件就不声明这项能力。

## 5. 参与状态判断（可选）

要影响状态的话，改 `PresenceResolver.Resolve`。**插入位置决定优先级**，现有顺序是：暂停分享 → 锁屏 → 空闲 → 分类结果。在这里插入新规则，同时更新 `src/DeskPal.Client.Core/AGENTS.md` 和 `docs/architecture.md` 里的优先级说明。

## 6. 测试

在 `tests/DeskPal.Tests/PresenceTests.cs` 加用例：构造 `ActivitySnapshot`，调用 `PresenceResolver.Resolve`，断言状态。新信号和已有信号同时出现的组合（例如锁屏的时候摄像头也在用）至少测一个。

最后用 skill `deskpal-e2e-check` 在真机上触发一次这个信号，检查“我的状态”是否符合预期。