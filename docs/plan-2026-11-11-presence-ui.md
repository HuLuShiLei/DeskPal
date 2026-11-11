# 状态界面、Room 改名与分类口径

阶段 0 计划。确认前不改业务代码。桌宠不在这次范围内。界面技术选型见 [ui-tech-evaluation.md](ui-tech-evaluation.md)。

## 这次做什么

1. 所有者可以修改 Room 名称，其他成员马上看到新名称。
2. 拿掉麦克风判断。不再有「会议中」。
3. 即时通讯分开：办公软件算工作，个人聊天软件显示「在聊天」。
4. 状态文案改成书面说法；状态、应用名、标题分行显示，标题过长时截断。
5. 主窗口只看状态，Room 管理和本机设置分开。这一项随界面技术一起做。

## 分两批

| 批次 | 内容 | 和界面技术有关吗 |
|---|---|---|
| 第一批 | Room 改名接口和消息、拿掉麦克风、内置规则 v2、`PresenceFormatter` 改成分行输出、现有 WPF 界面做最小适配（Room 菜单加「修改名称」，卡片用新文案） | 无关 |
| 第二批 | 按选型结论重做界面：状态面板、Room 管理、设置三块 | 有关，先做技术验证 |

第一批不在 WPF 布局上多花工夫，界面拆分留到第二批。

## 这次不做什么

- 不做桌宠、不做按键统计。
- 不新增、不删除 `ActivityCategory` 和 `PresenceState` 的成员（协议兼容，见 rule `shared-protocol`）。
- 个人规则、Room 规则、内置规则的匹配顺序不变。
- 不改隐私级别的默认值，不改防抖时间，不改邀请码、人数上限、转让和解散的规则。
- 不上报原始窗口标题的条件不变：只有该 Room 把对应类别设成「显示详情」才带标题。
- 不改 Server 对 `presence.update` 的处理。Server 仍然不解析 Payload。

## 行为口径

### Room 改名

- 只有所有者可以改。其他成员调用时返回 403，「只有所有者可以修改 Room 名称」。
- 名称去掉首尾空白后为 1 到 30 个字符，和创建 Room 相同。不合规返回 400，「Room 名称需要 1 到 30 个字符」。不是成员返回 404。
- 允许和别的 Room 重名。名称没有变化时直接返回成功，不广播。
- 成功后 Server 广播 `room.renamed`。客户端收到后重新拉取 Room 列表，不弹托盘气泡。
- 旧客户端不认识这个消息类型，会忽略，下次启动才看到新名称。
- 不改数据库结构。`Rooms.Name` 已经存在。

### 拿掉麦克风判断

- 删除 `MicCollector`、`MicSignal`、`ActivitySnapshot.Mic`，以及 `PresenceResolver` 里的麦克风分支。客户端不再读取麦克风占用。
- 状态优先级变为：暂停分享 → 手动状态 → 锁屏 → 空闲（全屏播放视频时除外）→ 分类结果。
- 会议软件（Zoom、腾讯会议、Webex、Teams）的内置规则改为 `Work`，显示「工作中」。
- 新客户端不再产生 `Meeting`。枚举成员保留，因为旧客户端可能还会发；收到时按「工作中」显示。
- 分类纠正和隐私设置对话框不再列出「会议」这一类。已保存的隐私配置里 `Meeting` 那一项保留，不再生效。
- 手动状态预设里的「开会中」是用户自己选的文字，不受影响，保留。

### 即时通讯规则

不新增类别，只改内置规则的 `category`，`version` 从 1 改为 2。个人规则和 Room 规则优先级更高，已经纠正过的应用不受影响。

| 应用 | 现在 | 改为 | 成员看到 |
|---|---|---|---|
| 企业微信、钉钉、飞书、Lark、Slack、Teams | Communication | Work | 工作中 |
| 微信、QQ、TIM、Telegram、WhatsApp | Communication | 不变 | 在聊天 |
| Outlook、Foxmail、Thunderbird | Communication | Work | 工作中 |
| 网页邮箱（QQ 邮箱、网易邮箱、Outlook、Gmail） | Communication | Work | 工作中 |
| Discord | Social | 不变 | 在看社交网站 |
| Zoom、腾讯会议、Webex | Meeting | Work | 工作中 |

网页版跟客户端：`wx.qq.com`、`web.whatsapp.com` 保持 Communication；`slack.com` 改为 Work。`feishu.cn`、`docs.qq.com` 已经是 Work，不动。

「在聊天」不算放松，也不算工作，是单独的状态。隐私默认值不变，`Communication` 仍是只显示状态，对方看到「在聊天」，看不到是微信还是 QQ。

### 文案

`PresenceFormatter` 的用户可见文字：

| 现在 | 改为 |
|---|---|
| 忙 | 工作中 |
| 摸鱼中 | 在放松 |
| 类别名「开会」 | 不再显示 |
| 在沟通 / 沟通 | 在聊天 / 聊天 |
| 在看视频、在听音乐、在购物、在玩游戏、在看小说、在看新闻 | 保留 |
| 在刷社交网站 / 社交 | 在看社交网站 / 社交网站 |
| 正在使用 X | 保留「正在使用 X」 |
| 离开座位 | 暂时离开 |
| 离开（已锁屏） | 已锁屏 |
| 隐私级别「只显示忙 / 摸鱼」 | 只显示状态 |

放松类的类别名统称「放松」，例如纠正对话框里的分组。状态枚举的英文名不改。

### 分行显示

成员卡片和本机识别不再拼成「状态 · 程序：完整窗口标题」。`PresenceFormatter` 输出三个字段，界面按行排：

- **状态**：例如「工作中」「在看视频」「正在使用 XMind」
- **应用名**：状态里已经带了应用名（「正在使用 X」）就不再单独显示
- **标题**：窗口标题或媒体名，超过 36 个字符时截断并加省略号，灰色小字

本机另加一行来源：「内置规则」「个人规则」「Room 规则」「推测」，以及「稳定后同步」。

发给其他成员的 `Detail` 仍是完整标题，截断只在显示时做。第二批的 HTML 界面也直接用这三个字段，不在前端重写文案。

### 界面分工（第二批）

- **状态面板**（托盘单击打开）：当前 Room 下拉框；对方的状态卡片（三行 + 持续时长 + 「戳一下」）；本机摘要、暂停分享、「这条分错了」、手动状态；「该 Room 成员看到」的预览。
- **Room**：创建、加入、修改名称、生成邀请码、隐私设置、转让所有者、退出、解散。
- **设置**：开机自启、窗口置顶、检查更新、加入测试版、立即重启并更新。服务器地址只读展示，修改仍在登录窗口。

按选型结论，三块都做成 HTML 页面，放在同一个 WebView2 宿主窗口里切换。前端能脱离客户端、在浏览器里用模拟数据打开，方便 AI 截图和点击检查。

## 改动清单（第一批）

| 文件 | 改什么 |
|---|---|
| `src/DeskPal.Shared/Api/RoomDtos.cs` | 增加 `RenameRoomRequest` |
| `src/DeskPal.Shared/Messaging/MessageTypes.cs` | 增加 `room.renamed` |
| `src/DeskPal.Server/Features/Rooms/RoomService.cs` | 改名校验和更新 |
| `src/DeskPal.Server/Features/Rooms/RoomEndpoints.cs` | `PUT /api/rooms/{roomId}` |
| `src/DeskPal.Server/Features/Presence/RoomNotifier.cs` | 广播改名 |
| `src/DeskPal.Client/Services/ApiClient.cs` | 调用改名接口 |
| `src/DeskPal.Client/Services/RoomState.cs` | 收到 `room.renamed` 后刷新列表 |
| `src/DeskPal.Client/MainWindow.xaml.cs` | Room 菜单加「修改名称」，仅所有者可用 |
| `src/DeskPal.Client.Core/Pipeline/ActivitySignal.cs`、`ActivitySnapshot.cs` | 删除 `MicSignal` 和 `Mic` |
| `src/DeskPal.Client/Collectors/MicCollector.cs`、`App.xaml.cs` | 删除采集器及注册 |
| `src/DeskPal.Client.Core/Presence/PresenceResolver.cs` | 删除麦克风分支 |
| `src/DeskPal.Client.Core/Presence/PresenceFormatter.cs` | 新文案；输出状态、应用名、截断标题三个字段 |
| `src/DeskPal.Client/ViewModels/MainViewModel.cs`、`MainWindow.xaml` | 卡片和本机识别按三行显示 |
| `src/DeskPal.Client/Views/Dialogs.cs` | 纠正、隐私对话框不列出会议类 |
| `src/DeskPal.Server/Features/Rules/builtin-rules.json` | 上表中的类别，`version` 改为 2 |
| `tests/DeskPal.Tests` | 改名、优先级、文案、分行、相关分类用例 |
| `docs/architecture.md`、`docs/progress.md`、`.cursor/rules/client-core.mdc`、skill `deskpal-builtin-rules` 的分类口径、skill `deskpal-add-signal` 里的麦克风示例 | 确认后同步口径 |

第二批的改动清单见文末「第二批：HTML 界面」。

## 验收对照（第一批）

| 验收 | 怎么验证 |
|---|---|
| 所有者改名成功，其他成员看到新名称 | `RoomService` 测试 + `PresenceHubTests` 收到 `room.renamed` |
| 非所有者、空名称、超长名称、非成员被拒绝 | `RoomService` 测试 |
| 名称未变不广播 | Hub 测试 |
| 客户端不再读取麦克风；开语音或开会时按前台应用显示 | `PresenceTests`；代码里没有 `MicSignal` |
| 企业微信、Teams、Outlook、Zoom 显示工作中；微信、QQ 显示在聊天 | 分类测试，规则写成和 JSON 一致；`validate-rules.ps1` 通过 |
| 收到旧客户端发来的 `Meeting` 显示为工作中 | `PresenceTests` |
| 三个字段正确，标题超过 36 字截断，`Detail` 原文不变 | `PresenceTests` |
| 界面不再出现「忙」「摸鱼」「开会」「沟通」 | 测试断言文案；打开主窗口、纠正和隐私对话框看一遍 |

Apifox 不适用于这个仓库。

## 风险点（需要确认）

1. **邮箱改为工作中。** 原来和微信同属「沟通」。「在聊天」用在邮箱上不贴切，所以并入工作。
2. **改名仅所有者。** 若希望任何成员都能改，说一声。
3. **已保存的 Room 规则或个人规则里有 `Meeting` 的**，仍会命中，显示为工作中，不会报错。
4. **旧版客户端**仍会因麦克风发出 `Meeting`，对方显示工作中。两人都升级后不再出现。

## 第二批：HTML 界面

技术验证已完成，结果见 [ui-tech-evaluation.md](ui-tech-evaluation.md) 的「验证结果」。第一批已提交（`62f2199`）。

### 范围

- 新的面板窗口 `PanelWindow`（WPF 窗口 + 标准 `WebView2`）替换 `MainWindow`。页面内分「状态」「Room」「设置」三个标签页。托盘单击打开或隐藏，和现在一样。
- 内容按上文「界面分工（第二批）」。所有输入、确认、错误提示都在页面内完成，不弹系统对话框。错误文字用 Server 返回的中文 message。
- 登录窗口改为 HTML：单独的小窗口（`LoginWindow` + 标准 `WebView2`），加载同一份前端的登录页。登录发生在 Room、Hub 等服务启动之前，所以不和面板合用一个窗口。
- 更新的确认和提示（`UpdateInteraction`）改为页面内对话框：宿主发 `dialog`，前端回复 `dialog.answer`。从托盘菜单或后台检查触发时，先打开面板再显示对话框。本机没有 WebView2 时退回系统对话框。
- 托盘菜单仍是原生。
- 不做桌宠。以后做时用 WPF 原生透明窗口。

### 宿主和前端的消息

宿主推给前端：

| 类型 | 内容 |
|---|---|
| `state` | 完整的面板状态，每次变化推送整份，最多每 200 ms 一次 |
| `result` | 命令结果：`id`、`ok`、`error`（中文）、`data` |
| `dialog` | 宿主要求确认或提示：`id`、`kind`（`confirm` / `info` / `warning`）、`message`。前端用 `dialog.answer` 回复 `id` 和 `ok` |
| `login` | 登录页的初始值：服务器地址、上次的用户名 |

`state` 的字段：连接状态；我的显示名、本机识别（`PresenceView` 三行）、来源、「该 Room 成员看到」、暂停分享、手动状态和预设；Room 列表（名称、是否所有者、人数）和选中的 Room；成员卡片（三行、持续时长、是否在线）；选中 Room 的隐私设置（类别名、当前级别、可选级别）；「这条分错了」的目标应用和可选类别；设置（开机自启、窗口置顶、加入测试版、有更新待安装、版本号、服务器地址）。所有文字都由 C# 算好，前端不拼文案。

前端发给宿主（都带 `id`，宿主用 `result` 回复）：

| 类型 | 参数 | 对应现在的操作 |
|---|---|---|
| `ready` | | 页面加载完成，宿主推送第一份 `state` |
| `room.select` | `roomId` | 切换 Room |
| `room.create` / `room.join` | `name` / `code` | 创建、用邀请码加入 |
| `room.rename` | `roomId`、`name` | 修改 Room 名称 |
| `room.invite` | `roomId` | 生成邀请码；宿主复制到剪贴板，`data` 返回邀请码和过期时间 |
| `room.privacy` | `roomId`、各类别的级别 | 保存隐私设置 |
| `room.transfer` / `room.leave` / `room.dissolve` | `roomId`（转让另带 `userId`） | 转让、退出、解散 |
| `member.poke` | `roomId`、`userId`、`text` | 戳一下 |
| `presence.pause` / `presence.manual` | `paused` / `text`（空表示清除） | 暂停分享、手动状态 |
| `presence.correct` | `category`、`shareToRoom` | 这条分错了 |
| `settings.autoStart` / `settings.topmost` | `enabled` | 开机自启、窗口置顶 |
| `update.check` / `update.beta` / `update.restart` | `update.beta` 带 `enabled` | 检查更新、加入测试版、立即重启并更新 |
| `dialog.answer` | `dialogId`、`ok` | 回复宿主的确认框 |
| `auth.login` / `auth.register` | `serverUrl`、`userName`、`password`（注册另带 `displayName`） | 登录窗口 |

消息格式写在 C# 的契约类型和前端的 `bridge.ts` 里，两边一起改。

### 代码结构

| 位置 | 内容 |
|---|---|
| `src/DeskPal.Client.Core/Ui/` | `PanelState` 契约和 `PanelStateBuilder`：从 Room、成员状态、本机识别、隐私配置、设置生成 `state`。纯逻辑，可以用 xUnit 测 |
| `src/DeskPal.Client/Ui/PanelWindow.cs` | 宿主窗口：初始化 WebView2，用虚拟主机名加载安装目录里的 `web/`；隐藏 5 分钟后释放 WebView2，再打开时重新加载 |
| `src/DeskPal.Client/Ui/PanelBridge.cs` | 订阅 `RoomState`、`PresenceCoordinator`、`UpdateService` 的变化并推送 `state`；把命令分发给现有服务（逻辑从 `MainWindow.xaml.cs` 搬过来） |
| `src/DeskPal.Client/web/` | Vite + TypeScript 前端。`src/bridge.ts`（类型、真实桥、模拟桥），`src/mock/`（模拟数据），三个标签页的组件 |
| `src/DeskPal.Client/DeskPal.Client.csproj` | 前端源码有变化时先执行 `npm ci` 和 `npm run build`；`web/dist` 复制到输出目录的 `web/` |
| `src/DeskPal.Client/Ui/LoginWindow.cs` | 登录窗口，替换 `Views/LoginWindow.cs` |
| `src/DeskPal.Client/Views/UpdateInteraction.cs` | 确认和提示改为通过面板的页面内对话框 |
| 删除 | `MainWindow.xaml`、`MainWindow.xaml.cs`、`ViewModels/MainViewModel.cs`、`Views/Dialogs.cs`、`Views/LoginWindow.cs` |
| `.github/workflows/ci.yml` | 安装 Node；`vpk pack` 加 `-f webview2` |

其他约定：

- WebView2 用户数据目录放在 `%LOCALAPPDATA%\DeskPal\WebView2`，不放在 `current\` 下。
- Debug 构建下设置环境变量 `DESKPAL_UI_URL=http://localhost:5190`，面板改为加载 Vite 开发服务器，真客户端里也能热更新。
- 启动时没有 WebView2 Runtime：用系统对话框提示并给出下载链接。已登录时采集和同步照常，只是面板打不开；未登录时无法登录，退出程序。安装包会先装好 WebView2，正常不会遇到。

### 测试

- xUnit：`PanelStateBuilder`，覆盖没有 Room、未连接、暂停分享、手动状态、非所有者（不能改名、解散）、对方离线、长标题截断。
- 样例数据：测试用 `PanelStateBuilder` 生成几份 `state` 样例，和 `web/src/mock/` 里的 JSON 比较，不一致就失败并提示更新命令。模拟数据因此始终和 C# 输出的样子一致。
- 前端：`npm run build` 带 TypeScript 类型检查，进 CI。AI 用模拟数据在 Cursor 自带浏览器里逐页检查截图和命令。
- 端到端：按 skill `deskpal-e2e-check` 起真客户端，检查面板显示、改名、戳一下、暂停分享；skill 里的检查项随界面一起更新。

### 验收

| 验收 | 怎么验证 |
|---|---|
| 托盘单击打开面板，三个标签页内容齐全，旧主窗口的功能都在 | 浏览器检查 + 端到端 |
| Room 的创建、加入、改名、邀请码、隐私、转让、退出、解散在页面内完成，失败时显示 Server 的中文 message | 浏览器检查（模拟桥返回失败）+ 端到端抽查 |
| 文案和截断与第一批一致 | `PanelStateBuilder` 测试 |
| 面板隐藏 5 分钟后 WebView2 进程退出，再打开能正常显示 | 端到端，看进程 |
| 安装包带上 WebView2 检查 | CI 的 `vpk pack` 参数；nuspec 里有 `runtimeDependencies` |

### 已确认

1. 前端框架用 Preact。
2. 面板隐藏 5 分钟后释放 WebView2。
3. 登录窗口和更新确认框也改为 HTML，托盘菜单仍是原生。

### 自测结果（2026-11-11）

- `dotnet build` 0 警告，`dotnet test` 81 个全部通过（新增 `PanelStateBuilderTests` 8 个、`PanelFixtureTests` 3 个）。前端 `tsc --noEmit` 和 `vite build` 通过。`dotnet publish` 的输出里有 `web/index.html` 和 `web/assets/`。
- 浏览器（模拟数据，宽 404）：三个样例和登录页逐页看过；纠正、改名、邀请码、登录失败、`?fail` 的错误提示都正常，发出的命令参数和 `PanelBridge` 读取的一致。
- 真实客户端：面板停靠在右下角；对方卡片三行显示和持续时间正确；“该 Room 成员看到”按隐私级别显示；“戳一下”的文字送达；暂停分享、手动状态和清除立即送达；改名后对方收到 `room.renamed`，面板里的名称同步更新；面板隐藏 5 分钟后 DeskPal 的 WebView2 进程从 6 个降到 0 个，再从托盘打开能正常显示。
- 自测中发现并已修复：
  - 面板初次显示没有停靠右下角（窗口显示前 `Left` 是 `NaN`，不是 0）。
  - 对话框的 `autoFocus` 对动态插入的元素不生效，焦点留在背后的按钮上，打字时空格会再次触发该按钮。改为挂载后用 ref 聚焦，对话框打开时背后的内容设为 `inert`；危险操作默认聚焦“取消”。
  - 登录页的用户名由宿主在页面加载后传入，已有用户名时改为聚焦密码框。
  - 离线成员的“戳一下”改为禁用；暂停分享时隐藏“这条分错了”。
  - csproj 里 `LinkBase` 保留了 `dist` 一层目录；改用 `%(RecursiveDir)` 时要先收集到单独的项，否则会按已有的 `Content` 批处理，把 `WebView2Loader.dll` 也复制进 `web/`。
- 未完成：没有 WebView2 Runtime 的系统上安装。