---
name: deskpal-add-message-type
description: 给 DeskPal 的 Envelope 协议新增一种消息类型（例如表情回应、摸鱼同步提醒、游戏事件），覆盖 Shared 常量和 payload、Server 放行或广播、客户端处理和界面、测试。用户要加新的实时互动、Room 事件，或者 Server 要主动推送新通知时使用。
---

# 新增消息类型

先判断消息是谁发的：

- **客户端发、Server 转发给同一 Room 的其他成员**（例如 `interaction.poke`）：走第 1、2a、3、4 步。
- **Server 在某个业务动作后主动推送**（例如 `room.memberJoined`）：走第 1、2b、3、4 步。

## 1. Shared

`src/DeskPal.Shared/Messaging/MessageTypes.cs`：

```csharp
public const string Reaction = "interaction.reaction";   // 命名：<领域>.<动作>，小驼峰
public static readonly IReadOnlySet<string> ClientPublishable = new HashSet<string> { PresenceUpdate, Poke, Reaction };
```

Payload 定义成 `sealed record`，放在同一个文件里（或者领域自己的文件），只放必须的字段：

```csharp
public sealed record ReactionPayload(string Emoji, Guid? TargetUserId);
```

## 2a. Server 转发

加进 `ClientPublishable` 就够了。`PresenceHub.Publish` 会检查发送者是不是 Room 成员，覆盖 `SenderId` 和 `Ts`，然后转发给 Room 里的其他人。**不要在 Hub 里解析 Payload**，以后做端到端加密时 Payload 会变成密文。

需要 Server 记住最后一条（像 `presence.update` 那样让新连上来的人能拿到），再参照 `PresenceStore` 处理，并且只存在内存里。

## 2b. Server 主动推送

在 `RoomNotifier` 加一个方法，用 `ServerEnvelope(type, roomId, userId)` 或 `Envelope.Create(...)` 构造消息，发到 `Clients.Group(RoomNotifier.GroupName(roomId))`。在对应 Endpoint 的业务动作成功后调用它。

## 3. 客户端

1. `RoomState.Handle` 的 `switch` 加一个 `case MessageTypes.Reaction:`，用 `envelope.ReadPayload<ReactionPayload>()` 读取。**不认识的类型直接忽略**，保持旧客户端兼容。
2. 需要提示的，调用 `Notify?.Invoke(标题, 内容)`（会显示托盘气泡）；需要刷新界面的，触发 `Changed`。
3. 发送端：用 `HubService.TryPublishAsync(Envelope.Create(MessageTypes.Reaction, roomId, payload))`（返回 false 表示当前没连上），界面入口：`Ui/PanelBridge.HandleAsync` 加一个命令，`web/src/bridge.ts` 的 `Command` 加类型，再在页面里调用；页面要显示的数据加到 `PanelState`，并更新样例（`DESKPAL_UPDATE_FIXTURES=1`）。
4. 如果以后的游戏模块也要消费，让它订阅 `RoomState` 的事件，不要在 Hub 回调里直接写业务逻辑。

## 4. 测试和文档

- `tests/DeskPal.Tests/PresenceHubTests.cs` 加一个集成测试：A 发送，B 收到，且非成员发送会被拒绝。
- 用 skill `deskpal-e2e-check` 跑一遍，把模拟成员程序改成发送或接收新类型。
- 更新 `docs/architecture.md` 的消息类型表，在 `docs/progress.md` 记一条变更日志。