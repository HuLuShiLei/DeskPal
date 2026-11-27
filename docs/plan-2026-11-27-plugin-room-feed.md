# 插件读取房间成员和状态（插件协议双向）

来源：GitHub issue #28，Multica HL-2。基础分支 `agent/slim-agent-context`，开发分支 `agent/hl-2-plugin-bidirectional`。

## 做什么

- 用户插件在 hello 里声明想读房间（`"reads":["room"]`），并且用户在插件页给它开了「读取房间成员」后，DeskPal 通过插件的**标准输入**每行写一个 JSON，告诉它当前房间的成员列表和每个成员的状态。
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
  {"id":"…","name":"张三","owner":true,"me":false,"blocked":false,"state":"Slacking","category":"Game","app":"Steam","since":"2026-11-27T08:30:00Z","client":"windows"},
  {"id":"…","name":"我","owner":false,"me":true,"blocked":false,"state":"Working","category":"Work","app":null,"since":"2026-11-27T08:10:00Z","client":"linux"},
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
| `category` | `PresenceSnapshot.Category` | 对方已按隐私设置过滤：该类别设为「只显示状态」时为 `None` |
| `app` | `PresenceSnapshot.AppDisplayName` | 应用名或网站域名。对方该类别设为「只显示状态」时为 null，和面板一致 |
| `since` | `PresenceSnapshot.Since` | ISO 8601 UTC；离线且从没收到过状态时为 null |
| `client` | `PresenceSnapshot.Client` | `windows` / `mac` / `linux` / null |

### 边界：不给插件其他成员的窗口标题、媒体标题

- 房间消息里的标题只在 `PresenceSnapshot.Detail` 一个字段里（`PresenceResolver.BuildDetail` 生成，窗口标题、网页标题、插件补充的标题、媒体标题都放这里）。**`room` 消息没有 `detail` 字段，也不存在任何开关能把它打开**：对方把类别设为「显示详情」时，面板能看到标题，插件仍然看不到。
- `PresenceSnapshot.CustomText` 现在没有客户端会生成，也不给，避免以后有人往里放标题时顺带漏给插件。
- 自己（`me:true`）也按同样的白名单，状态取我发给这个房间的那份（`PrivacyFilter.Apply` 之后），不给自己的标题：插件要自己的窗口标题，本来就能自己读。
- 实现约束：构造 `room` 行的代码不能拿到 `Detail` 以外再去读标题的路径；单元测试用带 `Detail`、`CustomText` 的快照断言输出 JSON 里不出现这两个值（按字符串搜索，不只看字段名）。

### 授权

- 插件声明了 `reads:["room"]` 也不会自动拿到数据。插件页给这个插件多一个开关「读取房间成员」，默认关；打开时确认一次，说明「会把当前房间成员的名字和状态（不含标题）交给这个程序，它可能保存或发到别处」。
- 只给用户插件；官方插件不需要。
- 没授权的插件，标准输入和现在一样是关闭的（保持 `timeout` 之类的旧行为不变）。授权、取消授权、插件被关闭时，重启这个插件：取消授权后插件拿不到任何新数据。
- 授权状态存在 `AppSettings.Plugins` 的这一行里（本机设置，不同步到账号，不上传 Server）。

### 发送规则

- 触发：`RoomState.Changed`（成员、房间、他人状态、选中房间）和我自己的状态提交（`PresenceCoordinator` 发布到房间时）。
- 合并：同一插件 1 秒内最多写一行，取最新；内容和上一行完全相同不重发。
- 插件不读标准输入时 DeskPal 不能被卡住：写入异步进行，写不进去（管道满）就只保留最新一行等下次，不排队；写失败只记 Debug 日志，不影响插件的其余部分。
- 插件重启后（包括崩溃重启）收到 hello 时重新发一次完整快照。
- Server 不改：成员和状态客户端本来就有（`RoomState`），不新增任何上传。

## 验收标准

- AC-1：解析 `{"type":"hello",...,"reads":["room","foo"]}` 得到 `Reads = {room}`；不写 `reads` 时为空；`provides:[]` 加 `reads` 合法。验证：`ProviderProtocolTests` 新增用例，`dotnet test tests/DeskPal.Tests`
- AC-2：给构造函数一个房间（3 个成员：一个在线且快照带 `Detail="机密.docx"`、`CustomText="秘密"`，一个从没收到状态，一个被我屏蔽），输出的 JSON 只含上表字段；字符串里搜不到 `机密.docx` 和 `秘密`，也没有 `detail`、`customText` 键；没状态的为 `Offline`、`since:null`；屏蔽的只有 `id/name/owner/me/blocked`。验证：新的 `RoomFeedTests`
- AC-3：对方该类别为「显示详情」时，输出仍不含标题；为「只显示状态」时 `category` 为 `None`、`app` 为 null。验证：`RoomFeedTests`（用 `PrivacyFilter.Apply` 生成发送方快照）
- AC-4：没进房间时输出 `room:null, members:[]`。验证：`RoomFeedTests`
- AC-5：合并规则：1 秒内多次变化只写最后一次；内容相同不重写；写入阻塞时不阻塞调用方。验证：发送合并逻辑放在可测的类里（时间可注入），单元测试
- AC-6：未授权（声明了 `reads` 但开关没开，或没声明）的插件标准输入是关闭的，读 stdin 立即 EOF；授权后重启插件并在 hello 后 1 秒内收到一行 `room`。验证：`ProviderHost`/`ExternalProvider` 用测试脚本插件的集成测试；做不了自动化时按 AC-9 人工看
- AC-7：`check_provider.py` 认识 `reads`；插件声明了 `reads:["room"]` 时，检查工具往它的标准输入写一行示例 `room`，并提示「这个插件会读取房间成员和状态」。验证：用 AC-9 的示例插件跑 `python3 .claude/skills/deskpal-write-provider/scripts/check_provider.py <示例>`
- AC-8：文档同步：`docs/provider-guide.md` 新增「读取房间成员」一节（字段表、明确写「不包含窗口标题和媒体标题」、需要在插件页授权、标准输入怎么读的 Python 和 PowerShell 片段），`SKILL.md` 协议表和「改插件系统本身时」表补上，`plugin-examples/` 加一个只把成员状态摘要写到标准错误的示例。验证：评审看
- AC-9（需人工看）：本机起 Server 和模拟成员（skill `deskpal-e2e-check`），模拟成员把「在玩游戏」设为显示详情并上报带标题的状态；本机放 AC-8 的示例插件，打开插件、打开「读取房间成员」并确认后，插件日志（设置 → 日志文件）里出现成员和状态，没有标题；成员切换状态后几秒内日志更新；关掉「读取房间成员」后不再更新。至少在一个平台跑；Windows 上 `.ps1` 插件读 stdin 要额外看一次
- AC-10（需人工看）：插件页里声明了 `reads` 的插件显示「读取房间成员」开关，中英文文案都有，打开时弹确认；未声明的插件不显示。验证：`npm run dev` 模拟数据（重新生成 fixtures）截图
- AC-11：三个客户端都编译通过（`AGENTS.md` 的编译表），`dotnet test tests/DeskPal.Tests` 全过

## 改哪里

- `src/DeskPal.Client.Core/Providers/ProviderProtocol.cs`：hello 解析 `reads`；新增 DeskPal → 插件消息的常量和说明
- `src/DeskPal.Client.Core/Providers/`（新文件，如 `RoomFeed.cs`）：从 `RoomDto`、成员快照、我的快照构造 `room` 行，白名单在这里；发送合并逻辑（如 `RoomFeedThrottle`）
- `src/DeskPal.Client.App/Providers/ExternalProvider.cs`：授权时不关闭标准输入；对外提供「写最新一行」；hello 时通知宿主补发；hello 的 `reads` 一并通过 `ProvidesAnnounced` 记住
- `src/DeskPal.Client.App/Providers/ProviderHost.cs`：授权开关命令、授权变化时重启插件；把房间快照分发给已授权且运行中的插件
- `src/DeskPal.Client.App/Providers/`（新文件或放在 ProviderHost 旁）：订阅 `RoomState.Changed` 和 `PresenceCoordinator` 的发布，生成快照交给宿主。注意 DI 不要形成循环
- `src/DeskPal.Client.App/Services/AppSettings.cs`：`PluginSetting` 加 `Reads`（上次 hello）和 `RoomAccess`（默认 false），新字段放最后
- `src/DeskPal.Client.App/Services/PresenceCoordinator.cs`：暴露我发给当前房间的那份过滤后快照（或发布事件），供 `me` 使用
- `src/DeskPal.Client.Core/Ui/PanelState.cs`：`PluginView` 加 `Reads`、`RoomAccess`
- `src/DeskPal.Client/web/src/`：`bridge.ts` 类型和 `plugins.setRoomAccess` 命令；`SettingsPage.tsx` 的 `PluginsPage` 开关和确认；中英文 `plugins.*` 文案（按 skill `deskpal-ui-copy`）；重新生成模拟数据
- `src/DeskPal.Client.Core/Localization`（或现有文案位置）：确认框文案中英文
- `tests/DeskPal.Tests/Providers/`：`ProviderProtocolTests` 增补，新增 `RoomFeedTests`、合并逻辑测试
- `.claude/skills/deskpal-write-provider/SKILL.md`、`scripts/check_provider.py`、`docs/provider-guide.md`、`plugin-examples/`（新示例和 `README.md`）
- `docs/architecture.md` 插件一节补一句「插件可经授权读取房间成员和状态，不含标题」；`CHANGELOG.md` 按项目惯例

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
- 1 秒合并一次；授权、取消授权都重启插件。
- 授权只存本机，不跟随「同步到账号」。

## 需要你定的

1. 插件拿房间数据要不要单独授权？推荐：**要**，插件页单独一个「读取房间成员」开关，默认关，打开时确认。理由：开启插件只说明信任它读本机，房间成员的名字和状态是别人的数据，插件拿到后能保存或上传，应当让你单独点头。
2. 成员的应用名、网站域名（`app`）给不给插件？推荐：**给**，和面板一致：只有对方把这个类别设为「显示应用」及以上时才有值，标题始终不给。理由：「在玩什么游戏」「在看哪个网站」是这个功能最常用的信息，不是标题；不给的话插件只能知道「在玩游戏」。代价：插件能低成本记录别人用过的应用和网站域名。
3. 其他成员要不要知道有人用插件读取房间？推荐：**不做**。理由：插件拿到的不超过面板上能看到的内容（还少了标题），提示会打扰别人，也需要改 Server。代价：成员不知道自己的状态可能被别人的插件记录。

## 验证档位

大改：改了插件协议和插件宿主（通信协议类）。需要按 skill `deskpal-e2e-check` 做端到端自检（AC-9），要在本机起 Server 和真实客户端；如果本机已有 DeskPal 实例在运行，需要你先退出。

## 建议分派

高难开发：大改档位，涉及子进程标准输入的异步写入、授权变化时重启、跨 RoomState / PresenceCoordinator / ProviderHost 的订阅，并且三个平台都要编译。

## 建议

- 以后要做「插件给房间发提醒」这类写能力时，沿用同一个 `reads` / 授权的思路另加 `writes`，并且一定经过 Hub 的已有消息类型，不让插件直接拿 token。
