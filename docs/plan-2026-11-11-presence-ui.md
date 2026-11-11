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

第二批的改动清单在技术验证之后另写。

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