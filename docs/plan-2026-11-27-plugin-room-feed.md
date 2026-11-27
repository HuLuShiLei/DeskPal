# 插件读取房间成员和状态（插件协议双向）

来源：GitHub issue #28，Multica HL-2。基础分支 `agent/slim-agent-context`，开发分支 `agent/hl-2-plugin-bidirectional`。

## 做什么

- 用户插件在 hello 里声明想读房间（`"reads":["room"]`），并且插件本身已开启，DeskPal 就通过插件的**标准输入**每行写一个 JSON，告诉它当前房间的成员列表和每个成员的状态。
- 插件刚启动（收到 hello）时发一次完整快照，之后房间、成员、任何成员（包括自己）的状态变化时再发完整快照。不做增量，插件每次直接用最新一行。
- 第一期只做这一种「DeskPal → 插件」消息。插件不能通过它发消息给房间、不能改别人的东西。

## 口径

### 协议字段（白名单）

插件 → DeskPal，hello 新增可选字段：

```json
{"v":1,"type":"hello","name":"房间提醒","version":"0.1","provides":[],"reads":["room"]}
```

- `reads`：插件想读的东西，目前只认 `room`，不认识的名字忽略。不写等于不读。
- 只声明 `reads`、`provides` 为空列表的插件合法（纯读取插件不抢任何能力）。

DeskPal → 插件，写到插件标准输入：

```json
{"v":1,"type":"room","room":{"id":"…","name":"摸鱼小组"},"members":[
  {"id":"…","name":"张三","owner":true,"me":false,"blocked":false,"state":"Slacking","category":"Game","since":"2026-11-27T08:30:00Z","client":"windows"},
  {"id":"…","name":"我","owner":false,"me":true,"blocked":false,"state":"Working","category":"Work","since":"2026-11-27T08:10:00Z","client":"linux"},
  {"id":"…","name":"李四","owner":false,"me":false,"blocked":true}
]}
```

没有进入房间时：`{"v":1,"type":"room","room":null,"members":[]}`。

每个成员**只有**下面这些字段，由 Client.Core 里的构造函数逐个从已有数据拷出来，不序列化整个 `PresenceSnapshot`：

| 字段 | 来源 | 说明 |
| --- | --- | --- |
| `id` | `RoomMemberDto.UserId` | |
| `name` | `RoomMemberDto.DisplayName` | 面板上显示的名字 |
| `owner` | `Role == Owner` | |
| `me` | `UserId == MyUserId` | |
| `blocked` | `RoomDto.Blocked` | 我屏蔽了的人：只有前四个字段和 `blocked:true`，**不带任何状态字段**，和面板「已屏蔽」一致 |
| `state` | `PresenceSnapshot.State` | 英文枚举名（和房间消息一致）。没收到过状态时为 `Offline` |
| `category` | `PresenceSnapshot.Category` | 和面板一致：类别属于状态本身，任何隐私级别都有（`PrivacyFilter.Apply`）；没有类别的状态（暂停、锁屏、暂离、离线）为 `None` |
| `since` | `PresenceSnapshot.Since` | ISO 8601 UTC；离线且从没收到过状态时为 null |
| `client` | `PresenceSnapshot.Client` | `windows` / `mac` / `linux`；离线或不是这三种时为 null（Server 不解析 Payload，值也要限定） |

**没有 `app`**（2026-11-27 用户决定）：成员的应用名和网站域名（`PresenceSnapshot.AppDisplayName`）不给插件，即使对方设为「显示应用」或「显示详情」。插件只知道对方「在玩游戏」，不知道玩的是哪个。

### 边界：不给插件其他成员的窗口标题、媒体标题

- 房间消息里的标题只在 `PresenceSnapshot.Detail` 一个字段里（`PresenceResolver.BuildDetail` 生成，窗口标题、网页标题、插件补充的标题、媒体标题都放这里）。**`room` 消息没有 `detail` 字段，也不存在任何开关能把它打开**：对方把类别设为「显示详情」时，面板能看到标题，插件仍然看不到。
- `PresenceSnapshot.CustomText` 现在没有客户端会生成，也不给，避免以后有人往里放标题时顺带漏给插件。
- `PresenceSnapshot.AppDisplayName` 也不读（见上，用户决定 `app` 不给）。
- 自己（`me:true`）也按同样的白名单，状态取我发给这个房间的那份（`PrivacyFilter.Apply` 之后），不给自己的标题：插件要自己的窗口标题，本来就能自己读。
- 实现约束：构造 `room` 行的代码不能拿到 `Detail` 以外再去读标题的路径；单元测试用带 `Detail`、`CustomText`、`AppDisplayName` 的快照断言输出 JSON 里不出现这三个值（按字符串搜索，不只看字段名）。输出不转义中文（`UnsafeRelaxedJsonEscaping`），所以按原文搜索有效。

### 授权（2026-11-27 用户决定：不单独授权）

- 没有「读取房间成员」开关，也没有确认框。插件在 hello 里声明了 `reads:["room"]`、并且插件本身已开启，就给它推送 `room`。插件被关闭时进程结束，自然拿不到新数据。
- 只给用户插件；官方插件不需要。
- 没声明的插件，标准输入和现在一样是关闭的（保持旧行为不变）。
- 开不开标准输入要在启动进程时决定，而 `reads` 在 hello 里才知道：上一次 hello 的 `reads` 记在 `AppSettings.Plugins` 这一行的 `Reads`（本机设置，不同步到账号，不上传 Server），启动时按它决定；hello 声明的和启动时不一致（新插件第一次运行、插件改了声明）就立即重启一次。
- 插件页对声明了 `reads:["room"]` 的插件显示一条说明「开启后会读取当前房间成员的名字和状态，不含窗口标题和媒体标题。」，折叠时第二行加「读取房间成员」。只是说明，不是开关。

### 发送规则

- 触发：`RoomState.Changed`（成员、房间、他人状态、选中房间）和我自己的状态提交（`PresenceCoordinator` 发布到房间时）。
- 合并：同一插件 1 秒内最多写一行，取最新；内容和上一行完全相同不重发。
- 插件不读标准输入时 DeskPal 不能被卡住：写入异步进行，写不进去（管道满）就只保留最新一行等下次，不排队；写失败只记 Debug 日志，不影响插件的其余部分。
- 插件重启后（包括崩溃重启）收到 hello 时重新发一次完整快照。
- Server 不改：成员和状态客户端本来就有（`RoomState`），不新增任何上传。

## 验收标准

- AC-1：解析 `{"type":"hello",...,"reads":["room","foo"]}` 得到 `Reads = {room}`；不写 `reads` 时为空；`provides:[]` 加 `reads` 合法。验证：`ProviderProtocolTests` 新增用例，`dotnet test tests/DeskPal.Tests`
- AC-2：给构造函数一个房间（成员：一个在线且快照带 `Detail="机密.docx"`、`CustomText="秘密"`、`AppDisplayName="Steam"`，一个从没收到状态，一个被我屏蔽），输出的 JSON 只含上表字段；字符串里搜不到 `机密.docx`、`秘密`、`Steam`，也没有 `detail`、`customText`、`app` 键；没状态的为 `Offline`、`since:null`；屏蔽的只有 `id/name/owner/me/blocked`。验证：新的 `RoomFeedTests`
- AC-3：对方该类别为「显示详情」时，输出仍不含标题和应用名；为「只显示状态」时 `category` 仍是真实类别（和面板一致），没有应用名。验证：`RoomFeedTests`（用 `PrivacyFilter.Apply` 生成发送方快照）
- AC-3b：任何情况下输出里都不出现 `app` 字段。验证：`RoomFeedTests`
- AC-4：没进房间时输出 `room:null, members:[]`。验证：`RoomFeedTests`
- AC-5：合并规则：1 秒内多次变化只写最后一次；内容相同不重写；写入阻塞时不阻塞调用方。验证：发送合并逻辑放在可测的类里（时间可注入），单元测试
- AC-6：没声明 `reads` 的插件标准输入是关闭的，读 stdin 立即 EOF；声明了 `reads:["room"]` 的插件（已开启）在 hello 后 1 秒内收到一行 `room`，房间变化后收到新的一行；以前声明过、这次不再声明的插件重启后标准输入关闭。验证：`ExternalProvider` 用测试脚本插件的集成测试（`ExternalProviderInputTests`）
- AC-7：`check_provider.py` 认识 `reads`；插件声明了 `reads:["room"]` 时，检查工具往它的标准输入写一行示例 `room`，并提示「这个插件会读取房间成员和状态」。验证：用 AC-9 的示例插件跑 `python3 .claude/skills/deskpal-write-provider/scripts/check_provider.py <示例>`
- AC-8：文档同步：`docs/provider-guide.md` 新增「读取房间成员」一节（字段表、明确写「不包含窗口标题和媒体标题」、不需要单独授权、标准输入怎么读的 Python 和 PowerShell 片段），`SKILL.md` 协议表和「改插件系统本身时」表补上，`plugin-examples/` 加一个只把成员状态摘要写到标准错误的示例。验证：评审看
- AC-9（需人工看）：本机起 Server 和模拟成员（skill `deskpal-e2e-check`），模拟成员把「在玩游戏」设为显示详情并上报带标题的状态；本机放 AC-8 的示例插件并开启后，插件日志（设置 → 日志文件）里出现成员和状态，没有标题和应用名；成员切换状态后几秒内日志更新；关掉插件后不再更新。至少在一个平台跑；Windows 上 `.ps1` 插件读 stdin 要额外看一次
- AC-10（需人工看）：插件页没有「读取房间成员」开关和确认框；声明了 `reads` 的插件展开后显示一条说明（中英文），折叠时第二行有「读取房间成员」；未声明的插件不显示。验证：`npm run dev` 模拟数据（重新生成 fixtures，样例「房间摘要」）截图
- AC-11：三个客户端都编译通过（`AGENTS.md` 的编译表），`dotnet test tests/DeskPal.Tests` 全过

## 改哪里

- `src/DeskPal.Client.Core/Providers/ProviderProtocol.cs`：hello 解析 `reads`；新增 DeskPal → 插件消息的常量和说明
- `src/DeskPal.Client.Core/Providers/`（新文件，如 `RoomFeed.cs`）：从 `RoomDto`、成员快照、我的快照构造 `room` 行，白名单在这里；发送合并逻辑（如 `RoomFeedThrottle`）
- `src/DeskPal.Client.App/Providers/ExternalProvider.cs`：上次声明了 `reads` 时不关闭标准输入，hello 后写最新一行；hello 和启动时不一致就重启一次；hello 的 `reads` 一并通过 `ProvidesAnnounced` 记住；声明了 `reads` 的插件多一条 `plugin.readsRoom` 说明
- `src/DeskPal.Client.App/Providers/ProviderHost.cs`：把 `RoomFeedSource` 和记住的 `Reads` 交给用户插件
- `src/DeskPal.Client.App/Providers/`（新文件或放在 ProviderHost 旁）：订阅 `RoomState.Changed` 和 `PresenceCoordinator` 的发布，生成快照交给宿主。注意 DI 不要形成循环
- `src/DeskPal.Client.App/Services/AppSettings.cs`：`PluginSetting` 加 `Reads`（上次 hello），新字段放最后
- `src/DeskPal.Client.App/Services/PresenceCoordinator.cs`：暴露我发给当前房间的那份过滤后快照（或发布事件），供 `me` 使用
- `src/DeskPal.Client/web/src/`：`SettingsPage.tsx` 的 `pluginSummary` 加「读取房间成员」；中英文 `plugins.note.plugin.readsRoom`、`plugins.summary.readsRoom` 文案（按 skill `deskpal-ui-copy`）；重新生成模拟数据。`PluginView`、`bridge.ts` 不变（说明走已有的 `PluginNote`）
- `tests/DeskPal.Tests/Providers/`：`ProviderProtocolTests` 增补，新增 `RoomFeedTests`、合并逻辑测试
- `.claude/skills/deskpal-write-provider/SKILL.md`、`scripts/check_provider.py`、`docs/provider-guide.md`、`plugin-examples/`（新示例和 `README.md`）
- `docs/architecture.md` 插件一节补一句「插件声明 `reads` 后可读取房间成员和状态，不含标题和应用名」；`CHANGELOG.md` 按项目惯例

## 不改什么

- Server、Hub、`Envelope`、`MessageTypes`、`PresenceSnapshot`：不新增消息类型，不改 Payload，硬约束 3 不受影响
- `PrivacyFilter` 和发送给房间的内容不变
- 官方插件、`CapabilityRouter`、`DetailRouter` 的路由规则不变
- 协议版本 `v` 和 `DESKPAL_PROTOCOL` 保持 1（只加可选字段，旧插件不受影响）
- 不给插件发消息到房间、私信、点赞之类的「写」能力

## 替你定的

- 推送完整快照而不是让插件发「查询」请求：收到 hello 就发一次，等于查询；之后变化再发，插件写法最简单。以后要真正的查询命令再加。
- 字段名用短英文（`id`、`name`、`state`…），状态和类别用房间消息里的英文枚举名，不给中文显示文字（显示文字可能拼进标题）。
- 自己也在成员列表里（`me:true`），给的是别人看到的我，不是本机完整状态。
- 屏蔽的成员列出但不带状态，和面板一致。
- 不给 `CustomText`（目前没有客户端产生它）。
- 1 秒合并一次；`reads` 声明变化时重启插件一次。
- 记住的 `Reads` 只存本机，不跟随「同步到账号」。

## 已定（2026-11-27 用户决定，覆盖原推荐）

1. 插件拿房间数据**不单独授权**：去掉「读取房间成员」开关和确认框，插件声明了 `reads:["room"]` 且已开启就推送。
2. 成员的应用名、网站域名（`app`）**不给**插件；`detail`、`customText` 同样没有。
3. **不提示**其他成员，不改 Server。

## 实现时更正

- 原文写「只显示状态」时 `category` 为 `None`。实际 `PrivacyFilter.Apply` 在任何隐私级别都发类别（类别属于状态本身，见 `Client.Core/AGENTS.md`「隐私过滤」），面板也显示类别。按「和面板一致」的原则，`category` 是真实类别，AC-3 已改。

## 验证档位

大改：改了插件协议和插件宿主（通信协议类）。需要按 skill `deskpal-e2e-check` 做端到端自检（AC-9），要在本机起 Server 和真实客户端；如果本机已有 DeskPal 实例在运行，需要你先退出。

## 建议分派

高难开发：大改档位，涉及子进程标准输入的异步写入、`reads` 声明变化时重启、跨 RoomState / PresenceCoordinator / ProviderHost 的订阅，并且三个平台都要编译。

## 建议

- 以后要做「插件给房间发提醒」这类写能力时，沿用同一个 `reads` 的思路另加 `writes`（届时再问用户要不要单独授权），并且一定经过 Hub 的已有消息类型，不让插件直接拿 token。
