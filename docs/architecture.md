# DeskPal 架构

描述当前代码的实际情况。和原方案 [plan-v1.md](plan-v1.md) 不一致的地方，以本文和 [progress.md](progress.md) 的“偏离方案”为准。

## 组成

| 项目 | 目标框架 | 职责 |
|---|---|---|
| `DeskPal.Shared` | net10.0 | Envelope、消息类型常量、状态和类别枚举、REST DTO、Hub 契约。Server 和客户端都引用 |
| `DeskPal.Server` | net10.0 | ASP.NET Core：Auth、Rooms、Presence（SignalR Hub）、Rules；SQLite |
| `DeskPal.Client.Core` | net10.0 | 不依赖 Windows 的纯逻辑：事件总线、分类器、状态判断、防抖、隐私过滤、面板状态（`Ui/PanelStateBuilder`） |
| `DeskPal.Client` | net10.0-windows10.0.19041.0 | WPF 托盘程序：采集器、Hub 连接、WebView2 宿主窗口，输出文件 `DeskPal.exe`。`web/` 是面板和登录页的前端（Vite + TypeScript + Preact），构建结果复制到输出目录的 `web/` |
| `DeskPal.Tests` | net10.0 | xUnit：分类、状态、Room 生命周期、Hub 集成测试 |

## 数据流

```mermaid
flowchart LR
  subgraph client [DeskPal.Client]
    Collectors["采集器（每秒轮询）"] --> Bus["ActivityBus"]
    Bus --> Coordinator["PresenceCoordinator"]
    Coordinator --> Classifier["LayeredClassifier"]
    Coordinator --> Resolver["PresenceResolver"]
    Resolver --> Debouncer["PresenceDebouncer"]
    Debouncer --> Filter["PrivacyFilter（当前房间）"]
    Bus -.->|"以后"| Game["游戏模块"]
  end
  Filter -->|"presence.update"| Hub["PresenceHub"]
  Hub -->|"Group room:{id}"| Others["同一 Room 的其他成员"]
  Endpoints["REST Endpoints"] --> Notifier["RoomNotifier"]
  Notifier --> Hub
```

1. 采集器把事实发布到 `ActivityBus`（基于 Channel 的一对多广播）。
2. `PresenceCoordinator` 把信号合并进 `ActivitySnapshot`，分类后得到 `LocalPresence`，交给防抖器。
3. 防抖器提交 `CommittedPresence` 以后，只按当前进入的房间做隐私过滤并发布。切换房间时，先给上一个房间发离线，再给当前房间发状态。已加入但没进入的房间留在列表里，再次进入不用邀请码。选「无房间」（`AppSettings.NoRoom`，或一个房间都没有）时 `RoomState.SelectedRoom` 为 null，所有房间都收到离线，之后不发布状态；选择、创建或加入房间时清除。分类和房间无关，切换房间不改变分类结果。
4. Server 只检查发送者是否为该房间的成员，然后转发给同一 Group 的其他连接，并把最新状态缓存在内存里。

## 采集信号

| 信号 | 来源 | 说明 |
|---|---|---|
| `ForegroundSignal` | `GetForegroundWindow` + 进程信息 + `FileVersionInfo` | 进程名、友好名称、窗口标题、是否全屏、是否浏览器 |
| `BrowserDomainSignal` | UI Automation 读地址栏 | 只取 host，3 秒超时，切换标签后最多约 2 秒才更新 |
| `IdleSignal` | `GetLastInputInfo` | 超过 `IdleMinutes`（默认 5）无输入 |
| `LockSignal` | `SystemEvents.SessionSwitch` | 锁屏和解锁 |
| `MediaSignal` | WinRT `GlobalSystemMediaTransportControlsSessionManager` | 是否在播放、标题、来源应用 |

## 分类

`LayeredClassifier` 按层匹配，命中即停止。

- 前台是浏览器：先查个人分类，没有命中时归为 `Browsing`，应用名用域名（没有域名则为空），详情用去掉浏览器后缀的窗口标题。不使用内置的网站和标题规则，也不因为全屏或正在播放而归成视频。个人分类里先按域名（取最具体的后缀），再按标题关键词，最后按进程名。`Shopping`、`Social`、`News`、`Reading` 的规则跳过，结果仍是正在浏览网页。
- 其他应用：个人分类 → 内置规则 → 推测 → `Other`，只按进程名匹配。
- 推测层只对非浏览器、且前几层都没命中时生效：路径含 `\steamapps\` 的程序归为 `Game`；全屏且正在播放媒体归为 `Video`。正在播放媒体本身不分类，避免后台音乐盖过当前焦点。

### 个人分类

- 「纠正分类」和「待归类」写入个人分类（`PUT /api/me/rules`），按账号保存在 Server 的 `UserRules` 表，所有房间、所有电脑通用，只影响自己的识别结果。没有房间规则：同一个应用，房间里每个人可以有不同的分类。
- 识别结果照常经过隐私过滤后广播，所以别人看到的是你自己的分类，例如“工作中 · 微信”。新加入的房间所有类别默认只显示状态，要在该房间把工作改成“显示类别和应用”，别人才看得到应用名。
- 修改后 Server 只给该用户自己的连接发 `rules.personalChanged`，各台电脑重新拉取。客户端在 `%APPDATA%\DeskPal\personal-rules.json` 缓存一份（带 userId，换账号时不用别人的缓存），离线启动时先用缓存。
- 每人最多 500 条；`Meeting` 和退役类别不能设置。「分类」页的「我的分类」可以搜索、修改和删除，状态页对当前应用提供「恢复默认」。

### 未识别应用和规则库候选

- `UnclassifiedUsage` 统计当天每个未识别桌面应用（落到 `Other` 层）的前台时长：两次记录间隔超过 30 秒的不计，本地零点清零。`UnclassifiedUsageStore` 每分钟和退出时写入 `unclassified-<userId>.json`，启动时只恢复当天的记录，重启后继续累计，上报的分钟数不会变小。浏览器网页不统计。状态页的「待归类」列出今天用了 5 分钟以上的前 5 个。
- 第一次打开面板时询问是否分享使用数据（`Users.ShareUsage`，空表示还没问过），设置页可以随时改。关闭时删除已上报的数据。
- 同意分享时，`UsageReporter` 每 2 小时上报今天用了 10 分钟以上的未识别应用（进程名、产品名、分钟数，`POST /api/me/usage`），不含窗口标题、网页和具体时间。
- `GET /api/rules/candidates` 只统计同意分享、注册满 `Rules:MinAccountAgeDays`（默认 3）天的账号，一个账号一票：
  - 多人一致：至少 `Rules:MinUsers`（默认 5）个账号设置过同一个应用，占比最高的类别不低于 `Rules:MinAgreement`（默认 0.8），且和内置规则不同。
  - 未识别应用：至少 `MinUsers` 个账号上报过，内置规则和任何人的个人分类都没有覆盖。
- 该接口要求请求头 `X-DeskPal-Maintainer` 等于 `Rules:MaintainerToken`，口令为空时返回 404。`.github/workflows/rule-candidates.yml` 每周请求一次，把多人一致的结果写进 `builtin-rules.json` 并开 PR，未识别应用列在 PR 说明里，人工审核后合并。
- 内置规则保存在 `builtin-rules.json`（目前版本 4，190 条，中文名称都带 `names.en`），Server 启动时写入数据库；客户端启动时带上已缓存的版本号请求，版本相同就不返回规则。里面的网站和标题规则不参与浏览器的默认分类。版本 3 起没有 `Music`、`Shopping`、`Social`、`News`、`Reading`。
- 分类口径：桌面应用里，企业微信、飞书、钉钉、Slack、Teams、邮箱和会议软件归为 `Work`；QQ、微信、Telegram 等个人聊天归为 `Communication`。浏览器里的网页不再按网站种类细分。音乐播放器、Kindle、calibre、Discord 没有单独类别，显示为“正在使用 XX”。

## 状态判断和防抖

`PresenceResolver` 的优先级：暂停分享 → 锁屏 → 空闲（`Away`，全屏播放视频时除外）→ 分类结果。

`PresenceDebouncer`：

- 新状态要持续 `StableSeconds`（10 秒，不写入 `settings.json`）才提交，`Since` 记为开始出现的时刻。`PresenceCoordinator` 在到期时刻再判定一次，不等下一次 5 秒的定时判定。
- 状态页「我的状态」大字显示已提交的状态，按当前房间的隐私设置过滤，和房间成员看到的一致；鼠标指向状态、应用名、标题时，用气泡显示不经过滤的类别、应用和标题（`PanelState.me.full`），只给自己看，不上报。无房间时不过滤。有待提交的新状态时，标题行右侧显示「n 秒后变为」和新状态（同样过滤），标题行不足 300px 时缩成「n 秒 →」，仍是一行；进度条贴在卡片底边，不改变卡片高度；`PanelState.me.pending` 给出剩余毫秒，页面自己倒数，指向倒计时显示新状态的完整内容（`pending.full`）和识别方式。房间成员还没有收到过状态时，右上角不显示文字。只有详情变了的，直接显示本机的新详情，不显示倒计时。
- 暂停分享、锁屏，进入和退出都立即提交。
- 状态不变、只是详情变了（例如切换到另一个视频），按同样的间隔限频。

不采集麦克风，也不再产生 `Meeting`。`Meeting` 留在 `PresenceState` 和 `ActivityCategory` 里，收到时显示为“工作中”。

## 界面文案

`PresenceFormatter.Format` 把状态拆成三行：标题（工作中、在聊天、正在浏览网页、正在使用 XX、在看视频、在放松、暂离、已锁屏等）、应用名、详情。浏览器的应用名是域名，详情是页面标题；页面上标题一行，应用名和详情合成一行（“应用 · 详情”），只有其中一个就显示那一个。应用名和详情去掉首尾空白后相同（不区分大小写）时，详情不再显示，避免出现“QQ · QQ”。`Tone`（work、chat、relax、browse、other、away、offline）决定标题、状态标签和头像底色的颜色。状态页成员行的「已持续多久」放在右侧按钮下方、和状态标签同一行，靠右对齐，位置不随状态文案长短和按钮数量变化。详情超过 36 个字时在界面上截断并加“…”，共享出去的 `Detail` 不截断。文案用书面用语，不用“忙”“摸鱼”。

## 多语言

客户端支持简体中文和英文，所有给用户看的文字都来自客户端，Server 不做多语言。

- 语言设置在 `settings.json` 的 `language`：`system`（默认，跟随 Windows 显示语言，中文显示语言用简体中文，其他一律英文）、`zh-CN`、`en`。启动时（登录窗口之前）由 `UiLanguage.Apply` 设置 `Strings.Culture`；设置页改了之后发 `settings.language`，立即生效：托盘菜单重设文字，`PanelState` 按新语言重新生成并推送。
- C# 生成的文字（`PanelState`、统计页、托盘、对话框、命令错误、更新提示、房间通知）在 `DeskPal.Client.Core/Localization/Strings.resx`（中文，中性资源）和 `Strings.en.resx`。日期、星期和时长的格式也在资源里，用 `UiLanguage.Format` 按当前语言格式化。
- 页面自己的文字（按钮、标题、对话框）在 `web/src/i18n/zh-CN.ts` 和 `en.ts`，用 `t(key, vars)`。初始语言来自页面地址的 `lang` 参数（`WebUi.Page` 加上），之后跟随 `PanelState.locale`；置顶小卡片跟随 `card` 消息的 `locale`。
- Server 的错误只返回错误码和参数：`{ "code": "room.full", "args": { "limit": "20" } }`，错误码在 `DeskPal.Shared/Api/ApiErrorCodes.cs`。客户端 `ApiErrorText` 按 `ApiError_<码>` 查资源并代入参数；不认识的错误码显示「请求失败（HTTP 状态码）」。
- 别人的客户端拒绝统计请求时，`stats.response` 带 `errorCode`（`not-shared`、`out-of-range`），请求方按自己的语言显示；旧客户端只带中文 `error`，照原样显示。
- 安装向导（Inno Setup）按 Windows 显示语言选择简体中文或英文。
- 内置规则的应用名：`name` 是默认名称（中文），`names` 是其他语言的名称（目前只有 `en`）。分类、上报、房间成员收到的、本机统计里存的都是默认名称；显示时 `AppNames.Localize` 按本机界面语言和本机缓存的规则库翻译（状态、置顶卡片、悬停说明、纠正分类、统计排行）。旧客户端忽略 `names`，照旧显示中文。
- 只有一种语言的内容：用户输入的名称（显示名称、房间名、个人分类的名称）、应用自己的产品名。更新条目在 `CHANGELOG.md` 里中英各写一行，按界面语言显示。


## 界面

托盘图标和托盘菜单是 WPF 原生的。面板和登录页是 HTML，放在标准 `WebView2` 控件里：

| 窗口 | 文件 | 内容 |
|---|---|---|
| 面板 | `Ui/PanelWindow.cs`、`Ui/PanelBridge.cs` | 标题栏中间是房间切换菜单。左侧导航五页：状态（我的状态、房间成员、待归类；公开了使用统计的成员行右侧有统计按钮）、统计（自己和房间成员的使用统计，以及查看记录、不记录标题、保留时长、导入导出、清除）、房间（列表、创建和加入、改名、邀请码、屏蔽成员、转让、退出、解散、隐私设置、公开使用统计）、分类（我的分类，可按应用、网站、类别搜索，修改和删除）、设置（账号和退出登录、开机自启、窗口置顶、关闭窗口时、夜间模式、分享使用数据、测试版、检查更新、问题与建议）。默认 420×680，可以拖动边框调整，最小 400×480，不能最大化；内容最宽 640px 居中。第一次打开时停靠在屏幕右下角。关闭按钮默认弹出选择：最小化，或最小化到系统托盘，可勾选不再提示；设置里可以改成每次询问、最小化或最小化到系统托盘。托盘菜单只有「打开 DeskPal」和「退出」 |
| 登录 | `Ui/LoginWindow.cs` | 登录和注册。在 Room、Hub 等服务启动之前显示，所以是单独的窗口 |
| 置顶小卡片 | `Ui/MemberCards.cs`、`Ui/MemberCardWindow.cs`、`PinnedMembers`（Client.Core） | 状态页成员行的大头针把该成员置顶到桌面，每人一张 280×84 的卡片（`index.html?page=card`），人数不限。卡片总在最前，不显示在任务栏，不抢焦点；从屏幕右上角往下排，整张可以拖动，双击打开面板，点 × 取消置顶。内容是头像、名字、已持续多久、状态标签，应用名和详情合成一行（C# 算好放在 `card` 消息里）。置顶只存在内存里，属于置顶时所在的房间：切换房间、选「无房间」、该成员退出、被踢出或被我屏蔽时移除，再进入也不恢复，重启后也没有 |

外观按 Windows 11 桌面程序做，不像网页：

- 没有系统标题栏。`Ui/WindowFrame.cs` 用 `WindowChrome`（`CaptionHeight` 0、`GlassFrameThickness` 0）去掉标题栏但保留窗口边框，并通过 DWM 要求圆角，Windows 11 上仍有圆角和阴影。
- 标题栏画在页面里（`TitleBar`）：CSS `app-region: drag` 的区域可以拖动窗口，依赖 WebView2 的 `IsNonClientRegionSupportEnabled`；最小化按钮发 `window.minimize`。面板的关闭按钮按 `CloseAction` 处理：`ask` 先在页面里选择，`minimize` 发 `window.minimize`，`tray` 发 `window.close`（面板只隐藏，不退出）。登录窗口的关闭直接发 `window.close`。
- 颜色默认跟随系统的浅色、深色模式。设置里的「夜间模式」可以改成固定浅色或固定深色，选择记在 `settings.json`。WPF 窗口背景使用同一次选择；跟随系统时仍读注册表 `AppsUseLightTheme`，避免加载时闪白。图标用 Segoe Fluent Icons；开关、设置行、菜单、对话框按 Windows 11 的控件样式写。
- 页面不能选中文字（输入框和 `.selectable` 除外），不显示手形光标，只在键盘操作时显示焦点框；关闭了缩放、滑动导航、自动填充和保存密码。
- 没有 WebView2 Runtime 时，面板退回一个 WPF 提示窗口，可以拖动和关闭。

- 前端从安装目录的 `web/` 加载，虚拟主机名 `app.deskpal.example`，不开本地端口。跳到其他来源的链接用系统浏览器打开。
- WebView2 用户数据目录：安装版是 `<安装目录>\WebView2`，直接运行时是 `%LOCALAPPDATA%\DeskPal\WebView2`。不放在 Velopack 每次更新都会替换的 `current\` 下。
- 面板隐藏 5 分钟后释放 WebView2，它的进程随之退出；再打开时重新加载，约 0.5 秒。
- 更新的确认和提示（`UpdateInteraction`）通过 `IUserPrompt` 显示在面板里；面板不可用时退回系统对话框。手动检查一点击，设置页就显示「正在检查更新…」，按钮变为「请稍候…」；开始下载后改为「正在下载新版本 x（百分比）」。检查更新不弹出 Windows 通知气泡。后台每 6 小时的检查不显示这些过程，下载完成后设置页的「立即重启并更新」变为可用。
- 更新内容：根目录 `CHANGELOG.md` 随客户端复制到程序目录。启动时 `WhatsNewStore`（Client.Core）把它和 `%APPDATA%\DeskPal\changelog-seen.json` 里看过的中文条目比较。每条下面一行 `en:` 是英文，放进 `PanelState.whatsNew` 时按当前界面语言选择。页面弹出「已更新到 x」和按版本分组的列表；点「知道了」发 `changelog.seen`，记下当前版本和全部中文条目。没有记录时（首次安装、第一次升级到有这个功能的版本）只记录不弹出。
- 启动时检测 WebView2 Runtime。没有时用系统对话框提示下载：已登录时采集和同步照常，只是面板打不开；未登录时无法登录。

宿主和页面之间用 JSON 消息通信（`PostWebMessageAsJson` / `chrome.webview.postMessage`），类型定义在 `web/src/bridge.ts`：

| 方向 | 消息 |
|---|---|
| 宿主 → 页面 | `state`（完整的 `PanelState`，变化后最多每 200 毫秒推一次）、`result{id, ok, error, data}`、`dialog{id, kind, message}`、`login{userName, notice}`（不带服务器地址，登录页不显示也不能修改，地址只在 `settings.json` 的 `serverUrl`） |
| 页面 → 宿主，要回复 | `{id, type, ...}`：`room.*`、`presence.pause / correct / resetCorrection`、`rules.set / delete`、`stats.query / export / import / clear / retention / noTitle.add / noTitle.remove`、`settings.autoStart / topmost / shareUsage / theme / closeAction / language`、`update.check / beta / restart`、`changelog.seen`、`auth.login / register / logout` |
| 页面 → 宿主，不回复 | `ready`、`dialog.answer{dialogId, ok}`、`window.minimize`、`window.close` |

`PanelState` 由 `PanelStateBuilder` 在 Client.Core 里生成，界面上的文字（连接状态、空提示、识别方式（悬停问号显示）、待提交状态、我的分类、待归类）都在 C# 里算好。状态页不显示「房间成员看到」，页面只负责显示。页面写「房间」，不写 Room。`PanelFixtureTests` 把几组样例按中文、英文各序列化一份到 `web/src/mock/fixtures/` 和 `fixtures/en/`，不一致就失败，改了 `PanelState` 或文案后用 `DESKPAL_UPDATE_FIXTURES=1` 运行测试更新。

在浏览器里调试：`cd src/DeskPal.Client/web; npm run dev`，打开 `http://localhost:5190/`。没有 `chrome.webview` 时页面用模拟宿主，参数 `?mock=default|no-room|guest` 切换样例，`?lang=en` 用英文样例，`stats.query` 返回 `fixtures/stats.json` 里的样例（只有「今天」所在的日、周、月），`?fail` 让所有命令失败，`?page=login` 打开登录页，发出的命令记录在 `window.__sent`。Debug 版客户端设置环境变量 `DESKPAL_UI_URL=http://localhost:5190/` 可以直接加载开发服务器。

## 使用统计

只在本机记录和计算，Server 不保存，也读不到内容。

- 记录：`PresenceCoordinator` 每次判定后交给 `UsageRecorder`（Client.Core）。状态、类别、应用、标题任一变化就开始新的片段；两次判定间隔超过 30 秒时中间不计；跨过本地零点时切开。暂停分享时按没有暂停时的判定记录。暂离、锁屏只记状态。标题最长 200 个字符，标题等于应用名时不记标题。「不记录标题」的规则（`NoTitleRule`）分四种：应用名（和状态页一致）、进程名（按 `RulePattern` 去掉 `.exe`）、网址（按域名，子域名也算；浏览器只拿得到域名，拿不到路径）、标题关键词（窗口标题、网页标题、媒体标题包含即算），命中时只记类别和名称。
- 存储：`SqliteUsageStore`，每个账号一个文件 `%APPDATA%\DeskPal\usage\<userId>.db`，不加密。表 `Segments`（片段，`DeviceId + StartAt` 唯一）、`DailyRollups`（过期日期的按天汇总）、`StatsViews`（谁查看过，保留 7 天）。正在进行的片段每分钟写一次结束时间。`UsageService` 每 6 小时把超过保留天数（默认 90）的片段折成按天汇总。
- 导入导出：JSON（`UsageExport`，`schemaVersion` 1，上限 100 MB），导出全部片段和汇总，导入时按 `DeviceId + StartAt` 去重。
- 显示：`UsageAggregator` 把片段汇总成 `UsageReport`（每天、每小时、时间轴、应用排行、标题排行），`StatsViewBuilder` 再按范围和细粒度生成页面要画的 `StatsView`，文字都在 C# 里算好。页面用手写 SVG 和 CSS 画时间轴、堆叠柱、图例和排行。
- 查看房间成员：成员在房间隐私设置里打开「公开使用统计」后，其他人通过 `stats.request` 向本人的客户端请求。请求带一次性的 ECDH P-256 公钥；本人的客户端检查自己当前进入的就是这个房间、请求方是成员且没有被屏蔽，把范围截到公开的天数，用 `StatsFilter` 按该房间每个类别的隐私级别降级，再用 AES-GCM 加密（密钥由 ECDH 结果经 SHA-256 派生，`RequestId` 作为附加数据，内容先 Brotli 压缩）后回复 `stats.response`。请求 30 秒没有回复就提示对方不在线或不在这个房间。双方都把结果缓存 60 秒。

## 隐私

- 隐私级别按 Room 和类别分别配置：`StateOnly`（只显示状态）、`Category`（再加类别和应用名）、`Detail`（再加窗口标题或媒体标题）。创建或加入房间时，Server 把所有类别写成 `StateOnly`。
- 默认值：`Work`、`Meeting`、`Communication` 为 `StateOnly`，其他类别为 `Category`。没有人默认开启 `Detail`。
- 过滤在客户端完成，原始标题不经过 PrivacyFilter 就不会离开本机。
- Envelope 的 `Enc` 字段预留给端到端加密，Server 不解析 Payload。`stats.response` 已经使用 `ecdh-p256-aesgcm`，其他消息仍是 `none`。
- 每个房间另有「公开使用统计」开关（`ShareStats`，默认关）和可查看天数（`StatsDays`，1、3、7、30，默认 7），保存在同一份隐私配置里。统计按上面的类别级别降级：`StateOnly` 的类别只给状态，`Category` 不给标题。

## REST 接口

除注册、登录、`/health`、内置规则和规则库候选外都要求 `Authorization: Bearer <token>`。错误返回 `{ "code": "错误码", "args": { ... } }`，不带给用户看的文字，见「多语言」。

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/auth/register` | 注册，返回 `AuthResponse`（含 token，有效期 30 天）。`Auth:RequireSignupInvite` 为 true 时必须带 `signupCode`：没填返回 400 `auth.signupCodeRequired`；不存在、已使用、已作废都返回 400 `auth.signupCodeInvalid`，不区分哪一种。创建账号和占用注册邀请码在同一个事务里，用条件更新占用，同一个码并发注册只有一个成功；用户名已被占用时不消耗注册邀请码。开关关闭时忽略 `signupCode` |
| POST | `/api/auth/login` | 登录。同一账号同一时间只能在一台电脑上登录：每次登录（和注册）生成新的 `Users.SessionId` 并写进 token（`sid`），之前登录的电脑收到 `auth.sessionReplaced`，2 秒后断开，它的旧 token 之后一律返回 401。部署前发出、不带 `sid` 的 token 在该账号下次登录前仍然有效 |
| GET | `/api/me` | 当前用户，含 `shareUsage`（空表示还没问过）和 `isAdmin`（用户名在 `Auth:AdminUserNames` 里）。登录、注册、改名的返回值同样带 `isAdmin` |
| GET | `/api/admin/signup-invites` | 管理员查看注册邀请码（`SignupInviteDto`），不含已作废的；未使用的在前，最多 100 条。已使用的带使用者的用户名和显示名称。非管理员返回 403 `auth.notAdmin`，下面两个接口相同 |
| POST | `/api/admin/signup-invites` | 管理员生成一个注册邀请码：8 位，字母表和房间邀请码相同，不过期 |
| DELETE | `/api/admin/signup-invites/{code}` | 管理员作废一个未使用的注册邀请码，返回 204；已使用的返回 409 `auth.signupCodeUsed`，不存在或已作废返回 404 `auth.signupCodeInvalid` |
| PUT | `/api/me/displayName` | 修改名称（`UpdateDisplayNameRequest`，去掉首尾空格后 1 到 20 个字符，允许重名）；名称有变化时给该用户所在的每个 Room 广播 `room.memberRenamed`，没有 Room 时只发给自己的连接 |
| GET | `/api/me/rules` | 我的个人分类，最近修改的在前 |
| PUT | `/api/me/rules` | 按（种类，标准化后的内容）新增或覆盖个人分类（`UpsertUserRuleRequest`），之后给自己的连接发 `rules.personalChanged` |
| DELETE | `/api/me/rules?kind=&pattern=` | 删除一条个人分类，返回 204 |
| PUT | `/api/me/sharing` | 是否分享使用数据（`SetSharingRequest`）；关闭时删除已上报的未识别应用 |
| POST | `/api/me/usage` | 上报今天的未识别应用（`UsageReportRequest`，最多 50 个，只收应用）。没有同意分享时返回 403 |
| GET | `/api/rooms` | 我加入的 Room。`blocked` 只列出我自己屏蔽的、仍在房间里的成员，不会出现别人的屏蔽记录 |
| POST | `/api/rooms` | 创建 Room（每人最多拥有 `MaxOwnedRoomsPerUser` 个） |
| POST | `/api/rooms/{id}/invites` | 生成邀请码，旧邀请码失效，24 小时过期。同时删除踢出记录，被踢出的人可以用新邀请码加入 |
| POST | `/api/rooms/join` | 用邀请码加入（人数上限 `MaxMembersPerRoom`，默认 20）。被踢出且邀请码尚未更新时返回 403 |
| POST | `/api/rooms/{id}/leave` | 退出；所有者退出返回 409。退出后可以用邀请码再次加入 |
| POST | `/api/rooms/{id}/kick` | 所有者踢出成员（`KickMemberRequest`）。对方从房间列表消失。生成新邀请码后可以再加入 |
| POST | `/api/rooms/{id}/blocks` | 屏蔽一名成员（`BlockMemberRequest`），返回 204。双向生效：我不再向对方发状态，也不再收到对方的状态。对方收到一条我的 `Offline`，看不出被屏蔽。屏蔽记录在退出后再加入时仍然有效 |
| DELETE | `/api/rooms/{id}/blocks/{userId}` | 取消屏蔽，返回 204。双方互相补发最新状态（对方也屏蔽了我时不补发） |
| POST | `/api/rooms/{id}/transfer` | 所有者转让所有权 |
| PUT | `/api/rooms/{id}` | 所有者修改 Room 名称（`RenameRoomRequest`，去掉首尾空格后 1 到 30 个字符）；名称有变化时广播 `room.renamed` |
| DELETE | `/api/rooms/{id}` | 所有者解散，级联删除成员、邀请码、踢出记录和屏蔽记录 |
| GET/PUT | `/api/rooms/{id}/privacy` | 我在该 Room 的隐私配置。`PrivacyProfileDto` 的 `shareStats`、`statsDays` 为空时保留原值；`statsDays` 只能是 1、3、7、30，否则返回 400。公开开关变化，或公开时天数变化，广播 `room.memberSharingChanged`。`RoomMemberDto` 带每个成员的 `shareStats`、`statsDays` |
| GET | `/api/rules/builtin?version=N` | 内置规则；版本相同时 `rules` 为 null |
| GET | `/api/rules/candidates` | 规则库候选（`RuleCandidatesResponse`），请求头 `X-DeskPal-Maintainer` 要等于 `Rules:MaintainerToken`，否则 404 |
| GET | `/health` | 健康检查，返回 `status` 和 `revision`（镜像构建时的 Git 提交号，本地运行为空） |

## Hub

路径 `/hubs/presence`，token 通过查询参数 `access_token` 传递。客户端调用 `Publish(Envelope)`，接收 `Receive(Envelope)`。`Envelope.To` 只用于定向消息（`MessageTypes.Direct`），其他消息 Server 会把它清空。单条消息上限 128 KB。

| 类型 | 发送方 | Payload | 说明 |
|---|---|---|---|
| `presence.update` | 客户端 | `PresenceSnapshot` | 客户端只向当前进入的房间发布，切换时向上一个房间发离线。Server 缓存最新一条；新连接会收到同房间其他人的缓存；最后一个连接断开时，Server 向该用户加入的每个房间广播 `Offline`。转发、补发缓存和离线广播都跳过与发送方互相屏蔽的成员（任一方屏蔽了另一方） |
| `stats.request` | 客户端 | `StatsRequestPayload` | 只发给 `To` 指定的成员。双方都要是该房间成员、`To` 不是自己、没有互相屏蔽，每人每分钟最多 10 次，否则不转发也不报错 |
| `stats.response` | 客户端 | `StatsResponsePayload`（`Enc` 为 `ecdh-p256-aesgcm`，或只带 `Error`） | 转发规则同 `stats.request`，不限次数 |
| `room.memberSharingChanged` | Server | `RoomEventPayload(userId)` | 成员打开、关闭公开使用统计，或公开时改了天数。客户端重新拉取房间列表，更新统计按钮 |
| `room.memberJoined` / `room.memberLeft` | Server | `RoomEventPayload(UserId)` | 同时增删 SignalR Group |
| `room.kicked` | Server | `RoomEventPayload(被踢出的人)` | 只发给被踢出的人。客户端提示邀请码更新后才能再次加入，并重新拉取房间列表 |
| `room.blocksChanged` | Server | `RoomEventPayload(被屏蔽的人)` | 只发给屏蔽者本人的所有连接，客户端重新拉取房间列表。被屏蔽的人不会收到任何房间事件 |
| `room.ownerChanged` | Server | `RoomEventPayload(新所有者)` | |
| `room.renamed` | Server | `RoomEventPayload(null)` | 客户端重新拉取 Room 列表 |
| `room.memberRenamed` | Server | `RoomEventPayload(userId)` | 成员改了显示名称。客户端重新拉取 Room 列表；是自己时（另一台电脑改的）先用 `/api/me` 更新本机保存的显示名称 |
| `room.dissolved` | Server | `RoomEventPayload(null)` | 之后所有成员移出 Group |
| `rules.personalChanged` | Server | `RoomEventPayload(本人)` | 只发给本人的所有连接，`RoomId` 为空 Guid。客户端重新拉取个人分类 |
| `auth.sessionReplaced` | Server | `RoomEventPayload(本人)` | 账号在另一台电脑登录。只发给之前登录的电脑，`RoomId` 为空 Guid，2 秒后 Server 断开这些连接。客户端清除 token，重启到登录页，提示「账号已在另一台电脑登录，这台电脑已退出。请重新登录。」没收到这条消息的（当时离线），之后重连或调用接口得到 401 时同样处理 |

## 数据模型（SQLite，本地 `storage/deskpal.db`，容器内 `/data/deskpal.db`）

| 表 | 主要字段 |
|---|---|
| `Users` | Id、UserName（唯一）、DisplayName、PasswordHash（PBKDF2）、CreatedAt、ShareUsage（空表示还没问过）、SessionId（当前登录的会话，空表示升级后还没登录过） |
| `Rooms` | Id、Name、OwnerId、MaxMembers、CreatedAt |
| `RoomMembers` | RoomId + UserId、Role、JoinedAt、PrivacyProfileJson（为空时用默认值） |
| `RoomInvites` | Code、RoomId、CreatedBy、ExpiresAt |
| `SignupInvites` | Code（唯一）、CreatedBy、CreatedAt、UsedBy、UsedAt、RevokedAt。注册邀请码，一个码注册一个账号，不过期 |
| `RoomBans` | RoomId + UserId、BannedBy、BannedAt。生成新邀请码时删除，被踢出的人即可用新邀请码加入 |
| `RoomBlocks` | RoomId + BlockerId + BlockedId、CreatedAt。成员之间的屏蔽，退出房间后保留，解散房间时级联删除 |
| `UserRules` | UserId + Kind + Pattern（唯一）、Category、Name、UpdatedAt。个人分类 |
| `UnknownAppReports` | UserId + Kind + Pattern、DisplayName、Minutes（最近一次上报的当天分钟数）、ReportedAt |
| `BuiltinRules` / `AppMeta` | 内置规则（`Names` 列是其他语言名称的 JSON）和规则库版本号 |

Server 启动时自动执行还没应用的迁移，部署不需要手动删除或重建数据库。

实时状态、连接、在线情况只在内存里（`PresenceStore`、`ConnectionRegistry`），Server 重启后清空。

## 客户端本地文件

设置和规则在 `%APPDATA%\DeskPal`，卸载不会删除。日志和 WebView2 缓存在安装目录（Velopack 的根目录，`current\` 的上一级）：自动更新时保留，卸载或在同一目录重新安装时随目录一起清空（安装向导会先保留 `logs\`）。直接运行或安装目录不可写时，日志回到 `%APPDATA%\DeskPal\logs`，缓存回到 `%LOCALAPPDATA%\DeskPal\WebView2`，见 `Services/AppPaths.cs`。以前版本写在 `%APPDATA%\DeskPal\logs` 的旧日志不迁移。

| 文件 | 内容 |
|---|---|
| `settings.json` | Server 地址（默认 `https://deskpal.hafuhafu.cn:36443`，界面上不显示也不能修改，联调时只能改这里）、当前用户、DPAPI 加密的 token、选中的 Room、是否为无房间（`noRoom`）、空闲和防抖参数、暂停分享、是否加入测试版（`includePrerelease`，空则沿用安装渠道）、是否分享使用数据（`shareUsage`，以 Server 为准）、关闭按钮（`closeAction`：`ask`、`minimize`、`tray`）、本机设备号（`deviceId`，写进统计片段）、统计保留天数（`usageRetentionDays`）、不记录标题的规则（`noTitleRules`，每条是 `kind` + `pattern`） |
| `usage\<userId>.db` | 使用统计（SQLite），见「使用统计」。卸载不删除，可以在统计页清除 |
| `personal-rules.json` | 个人分类的缓存（`userId` + `rules`），以 Server 为准 |
| `builtin-rules.json` | 内置规则缓存（含其他语言的名称） |
| `unclassified-<userId>.json` | 当天未识别应用的前台时长（`day` + `apps`），第二天启动时不再使用 |
| `<安装目录>\logs\deskpal.log` | 客户端运行日志。超过 1 MB 后改名为 `deskpal-时间.log`，最多保留 5 个旧文件，超过 7 天的旧文件删除。不记录窗口标题、媒体标题、域名、页面标题、密码、token、邀请码 |
| `<安装目录>\logs\setup.log` | 最近一次通过安装向导安装时 Velopack Setup.exe 的日志 |

`settings.json` 里还有窗口置顶（`topmost`）。WebView2 的缓存在 `<安装目录>\WebView2`。开机自启写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`；用命名 Mutex 保证单实例运行。

## 部署和发布

细节见 [deploy/README.md](../deploy/README.md)。

| 环节 | 做法 |
|---|---|
| Server 镜像 | `.github/workflows/docker.yml`：推送默认分支或 `v*` 标签时用 `deploy/Dockerfile.server` 构建，推到 `ghcr.io/hulushilei/deskpal-server` |
| Server 运行 | Portainer Stack（`deploy/stack.yml`），在 192.168.66.5 上发布 18820 端口；`Jwt__Key` 来自 Stack 环境变量 `DESKPAL_JWT_KEY`，`Rules__MaintainerToken` 来自可选的 `DESKPAL_MAINTAINER_TOKEN`；`Auth__RequireSignupInvite`、`Auth__AdminUserNames__0` 来自可选的 `DESKPAL_REQUIRE_SIGNUP_INVITE`、`DESKPAL_ADMIN_USER` |
| 规则库候选 | `.github/workflows/rule-candidates.yml`：每周一和手动运行，用 secret `DESKPAL_MAINTAINER_TOKEN` 请求 `/api/rules/candidates`，`.github/scripts/merge-rule-candidates.mjs` 合并进 `builtin-rules.json`，校验后开 PR（分支 `rules/candidates`） |
| 自动更新 | main 上的镜像同时推 `latest` 和 `sha-<提交号>`。GitHub Actions POST `PORTAINER_WEBHOOK_URL?tag=sha-<提交号>`，再等到 `/health` 的 `revision` 等于该提交号。Traefik 只在 36443 上公开 webhook token 路径 |
| HTTPS | 192.168.66.10 上已有的 Traefik：`deskpal.hafuhafu.cn` 走 443 和 36443 两个入口，`*.hafuhafu.cn` 泛域名证书（阿里云 DNS challenge）；路由文件 `/etc/traefik/conf/deskpal.yml`，仓库里的副本是 `deploy/traefik-dynamic.yml` |
| 客户端 | `.github/workflows/ci.yml`：main 上改了客户端时用 Velopack 发测试版 Pre-release（通道 `beta`，版本 `x.y.z-dev.<运行号>`，安装包 `DeskPal-<版本>-Setup.exe`，测试版版本号形如 `0.1.2-dev.15`），只保留最近 8 个；手动推送 `vX.Y.Z` 发正式版（通道 `win`，安装包同样是 `DeskPal-<版本>-Setup.exe`，例如 `DeskPal-0.1.2-Setup.exe`）。安装包是 Inno Setup 向导（`installer/DeskPal.iss`，CI 下载 Inno Setup 6.7.3 并校验 SHA256 后编译），内嵌 `vpk pack` 生成的 Setup.exe；用户选好目录后用 `--installto` 静默安装。Release 上只放这个向导，Velopack 自带的 Setup.exe 上传后删除，自动更新只用 nupkg 和 releases 清单。已安装的客户端启动后和每 6 小时向 GitHub Release 检查更新，后台下载；只在退出或选择立即重启时安装，启动时不自动安装。取消测试版会删除已下载的包。打包时带 `--framework webview2`，安装程序和每次更新前都会检查 WebView2 Runtime，缺少时自动安装。CI 先用 Node 22 构建 `web/`。Release 说明附带更新内容：测试版列出上一次发布以来的提交标题，正式版列出上一个正式版以来的提交标题。客户端弹出的更新内容另取自 `CHANGELOG.md`，CI 发布时要求最上面一节是本次的正式版版本号（测试版取 `x.y.z-dev.N` 的 `x.y.z`） |
