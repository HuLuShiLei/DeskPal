---
name: deskpal-add-signal
description: 给 DeskPal 客户端新增一种活动信号或采集器（例如摄像头占用、键盘输入次数、电源状态），从 ActivitySignal、采集器、ActivitySnapshot、PresenceResolver 一直到测试。用户要让状态判断多参考一种系统信息，或者游戏模块需要新的输入数据时使用。
---

# 新增活动信号

先确认这个信号用普通用户权限就能拿到，并且不会读到内容（见 rule `client-collectors`）。做不到就停下来，和用户商量替代方案。

## 1. 定义信号（Client.Core）

`src/DeskPal.Client.Core/Pipeline/ActivitySignal.cs`：

```csharp
public sealed record CameraSignal(bool InUse, string? AppName) : ActivitySignal;
```

只放事实，不放判断结果。键盘输入只能是**次数**，不能是按了什么键。

## 2. 并入快照

`ActivitySnapshot` 加属性并在 `Apply` 里加一个分支：

```csharp
public CameraSignal Camera { get; init; } = new(false, null);
...
CameraSignal c => this with { Camera = c },
```

只给游戏模块用的高频信号（例如输入次数）**不要并入快照**。游戏模块自己 `bus.Subscribe()`，否则每次输入都会让状态重新计算一遍。

## 3. 采集器（Client）

`src/DeskPal.Client/Collectors/CameraCollector.cs`，参照 `IdleCollector`：

- 继承 `BackgroundService`，注入 `ActivityBus` 和 `ILogger<T>`。
- 用 `PeriodicTimer` 轮询，只在值变化时 `bus.Publish(...)`。
- 系统调用包在 try/catch 里，失败只记 Debug 日志。
- 需要 P/Invoke 的，声明加在 `Native/NativeMethods.cs`。

在 `App.xaml.cs` 的 `BuildHost` 里注册：`services.AddHostedService<CameraCollector>();`

## 4. 参与状态判断（可选）

要影响状态的话，改 `PresenceResolver.Resolve`。**插入位置决定优先级**，现有顺序是：暂停分享 → 手动状态 → 锁屏 → 空闲 → 分类结果。在这里插入新规则，同时更新 rule `client-core` 和 `docs/architecture.md` 里的优先级说明。

## 5. 测试

在 `tests/DeskPal.Tests/PresenceTests.cs` 加用例：构造 `ActivitySnapshot`，调用 `PresenceResolver.Resolve`，断言状态。新信号和已有信号同时出现的组合（例如锁屏的时候摄像头也在用）至少测一个。

最后用 skill `deskpal-e2e-check` 在真机上触发一次这个信号，检查“我的状态”是否符合预期。