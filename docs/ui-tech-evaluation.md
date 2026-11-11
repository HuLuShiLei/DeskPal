# 客户端界面技术选型

2026-11-11。只评估界面层。采集器、分类、防抖、隐私过滤、SignalR 连接都是 C#，不在讨论范围内。

## 结论

建议采用 **.NET 宿主 + WebView2 的 HTML 界面**，不改成 Node.js / Electron，也不继续用 XAML 写界面。

- 托盘、采集器、`DeskPal.Client.Core`、Hub 连接、Velopack 更新都保留，仍是一个 Velopack 安装包。
- 状态面板、Room 管理、设置三个界面改成 HTML/TypeScript，放在 WebView2 里。
- 桌宠用 WPF 原生透明窗口。HTML 桌宠在功能上可行，但透明窗口里的 WebView2 即使画面不动也一直占 CPU，不适合常驻（见「验证结果」）。
- 用 AI 开发时，HTML 界面可以脱离客户端，在浏览器里用模拟数据打开，AI 能自己截图、读 DOM、点按钮、看控制台报错。这是三个方案里反馈最快的，也是选 C 的主要理由之一。

「先验证再定」的三项已在 2026-11-11 验证，结果见「验证结果」。

## 比较的三个方案

| | A. 继续 WPF | B. Electron（Node.js） | C. .NET + WebView2 |
|---|---|---|---|
| 采集器 | 不动 | 重写，或保留 C# 进程再加进程间通信 | 不动 |
| 和 Server 共用 `DeskPal.Shared` | 能 | 不能，DTO 要用 TypeScript 再写一遍 | C# 侧能；前端只看宿主给的 JSON |
| 写界面 | XAML，样式和动画费时间 | HTML/CSS | HTML/CSS |
| 安装包 | 约 64 MB（现状） | 约 100 MB 以上，再加 C# 进程 | 现状加几 MB |
| 面板打开时的内存 | 最低 | 最高 | 多出 WebView2 的几个进程，关掉面板时可以释放 |
| 桌宠（透明、置顶、点击穿透） | 成熟 | 成熟 | 透明可以；透明区域的点击要验证 |
| 运行条件 | 无 | 无 | 需要 WebView2 Runtime |
| 更新 | Velopack（现状） | 要换一套 | Velopack（现状） |
| AI 看到界面 | 只能截整个桌面再按坐标点 | 渲染进程可以用浏览器工具检查 | 前端在浏览器里打开，读 DOM、截图、点按钮 |
| AI 改完看效果 | 重新编译、重启客户端 | 热更新，但采集部分要连同 Node 进程一起起 | `npm run dev` 热更新，不用起客户端 |
| AI 自动测试 | xUnit 测逻辑；界面基本靠人看 | 界面能测，采集难测 | xUnit 测 C#；前端单元测试；浏览器里点一遍 |

### 为什么不选 Electron

- 采集用到 `GetForegroundWindow`、UI Automation 读地址栏、注册表、WinRT 媒体会话。Node 里要靠原生扩展，或者保留一个 C# 进程。前者维护成本高，也更容易被公司安全软件拦；后者就是两套运行时、两个进程，再加一层通信。
- 包体积和内存都会明显变大。对一个常驻托盘的程序是负担。
- `DeskPal.Shared` 里的 Envelope、DTO、枚举要在 TypeScript 里再维护一份。
- 现有 Velopack 更新、单实例、DPAPI 保存 token 都要重做。

它唯一的优势是 HTML 界面，而方案 C 也有。

### 为什么不继续 WPF

- 以后要做桌宠、动画和更像产品的界面。用 XAML 做这些，比 HTML/CSS 慢得多，样式也难调。
- 现在的对话框都在 `Views/Dialogs.cs` 里用代码拼出来，继续加 Room 管理和设置，界面会越来越像系统对话框。

它的优点是零新依赖、内存最低、透明窗口最成熟。所以桌宠验证失败时，桌宠窗口退回 WPF。

## AI 开发和测试的反馈

用 Cursor 开发时，AI 改完代码要能自己确认对不对。各方案能拿到的反馈不同。

**WPF。**AI 看不到窗口内容。只能用桌面截图加坐标点击，慢，坐标容易偏，而且要先编译、启动客户端、登录、进 Room 才能看到对应界面。XAML 写错的绑定在运行时才暴露，只在调试输出里打印一行，AI 不容易发现。界面问题大多要用户自己看了再描述。

**Electron。**渲染进程就是网页，AI 能检查。但采集器要用 Node 原生扩展或另起 C# 进程，出问题时要在两个运行时之间排查。

**WebView2 的 HTML 界面。**反馈链最短：

1. 前端单独运行：`npm run dev`，在 Cursor 自带浏览器里打开。宿主桥换成模拟实现，用固定的 JSON 数据模拟各种状态（离线、工作中、在放松、标题很长、没有 Room、未连接服务器）。
2. AI 能用 `browser_snapshot` 读结构、`browser_take_screenshot` 看样式、点击按钮、看控制台报错。改样式后热更新，几秒内看到结果。
3. 发给 C# 的命令在模拟桥里记录下来，AI 能核对点击「修改名称」之后发出的命令和参数。
4. C# 侧处理命令的代码是普通方法，用 xUnit 测，不用起界面。
5. 前后端对接放到最后，用现有的 skill `deskpal-e2e-check` 起真客户端走一遍。

为了让这条链成立，要守住两点：

- 宿主和前端之间的消息格式写成一份清单（类型名和字段），模拟桥和真实桥都照它实现。消息格式改了，两边一起改。
- 文案、截断、状态排序这类逻辑留在 C#（`PresenceFormatter`），前端只排版。这样文案能继续用 xUnit 断言，模拟数据也直接用 C# 输出的样子。

透明桌宠窗口这一块，浏览器里只能看动画本身，置顶、穿透、拖动仍要起真客户端看。这和 WPF 没有区别。

## 方案 C 的结构

- 一个 WPF 宿主窗口放 `WebView2`，加载安装目录里的前端文件（用虚拟主机名映射，不起本地 HTTP 端口）。
- C# 推给前端：把当前状态、Room 列表、成员卡片序列化成 JSON，用 `PostWebMessageAsJson` 发出去。卡片的文案和截断仍在 `PresenceFormatter` 里算好，前端只负责排版。
- 前端发给 C#：用 `chrome.webview.postMessage` 发命令（改名、生成邀请码、暂停分享、纠正分类等），C# 调用现有的 `ApiClient`、`RoomState`、`PresenceCoordinator`。
- 前端用 Vite + TypeScript 构建。框架在验证阶段定，倾向轻量的。
- 托盘菜单仍是原生的 `H.NotifyIcon`。
- CI 的客户端任务先构建前端，再 `dotnet publish`。

## 方案 C 的代价

- **WebView2 Runtime**：Windows 11 自带；Windows 10 的个人设备和公司域内设备，微软已经大面积推送。少数全新安装、LTSC 或精简版系统没有。Velopack 原生支持：`vpk pack` 加 `--framework webview2`，安装程序先检测，缺少时自动下载安装，更新前也会再检查一次。启动时仍要检测，失败就提示下载。
- **内存**：面板打开时会多出 WebView2 的浏览器、渲染、GPU 进程。面板隐藏一段时间后可以释放 WebView2，托盘常驻时不占这部分内存。
- **两种语言**：C# 加 TypeScript，构建多一步 `npm`。
- **交给 AI 写界面更顺手**：HTML/CSS 的样例和组件远多于 XAML，加上上面说的浏览器反馈，AI 能自己改、自己看。

## 先验证再定

| 验证项 | 通过标准 | 不通过怎么办 |
|---|---|---|
| Velopack 安装包 + WebView2 | CI 的 `vpk pack` 加 `--framework webview2`；在没有 WebView2 Runtime 的系统（例如 Windows 10 LTSC 虚拟机）上安装时自动装上；安装后和更新后都能显示 HTML 面板；记录面板打开、关闭时的内存和首次打开耗时 | 安装时不装，改为启动时提示下载；还不行就回到方案 A |
| 浏览器内开发 | 前端用模拟桥在 Cursor 自带浏览器里打开，AI 能截图、点击、读到发出的命令；改样式后热更新生效 | 先修开发环境再继续，不影响选型 |
| 透明桌宠窗口 | `WebView2CompositionControl` + `AllowsTransparency` 的窗口：背景完全透明；置顶；能拖动；透明区域点击穿透到下层窗口；一段 60 fps 的精灵动画 CPU 占用可以接受 | 桌宠用 WPF 原生窗口，面板仍用 HTML |

已查到的事实：标准 `WebView2` 控件放在 `AllowsTransparency` 的窗口里不显示，要用 `WebView2CompositionControl`；背景只支持完全透明，不支持半透明；透明窗口里 WebView2 的点击判定有已知问题（WebView2Feedback #997）。所以第二项必须实测。

## 验证结果

2026-11-11 在本机验证：16 个逻辑核，WebView2 Runtime 157，vpk 1.2.161。验证程序放在 `.local/tmp/`，用完已删除。下面的内存是私有内存。

| 验证项 | 结果 |
|---|---|
| Velopack 安装包 + WebView2 | 通过（缺少运行时的情况未实测）。`vpk pack -f webview2` 可用，安装包的 nuspec 里记下 `runtimeDependencies=webview2`，安装和每次更新前都会检查。本机已有运行时，静默安装 2 秒完成；装好的程序从 `current\web` 加载前端，481 ms 显示面板；卸载后快捷方式和目录都清掉了。没有运行时的系统要在第一次正式发布前用虚拟机装一次 |
| 浏览器内开发 | 通过。Vite 开发服务器加模拟桥，在 Cursor 自带浏览器里打开；AI 能读结构、截图、点击，用 `Runtime.evaluate` 读到发出的命令（`presence.pause`、`member.poke`）；改 CSS 后不刷新页面就生效 |
| 透明桌宠窗口 | 功能通过，开销不通过。背景透明；透明区域的点击落到下层窗口；按住桌宠拖动（前端发 `pet.dragStart`，C# 调 `DragMove`）可以移动；动画 61 fps。但 CPU 占一个核的 31%（宿主 16%、GPU 进程 13%、渲染 3%），把动画停掉仍占 23%。原因是这个控件不断抓取画面再交给 WPF，和页面动不动无关 |

对照数据：

| 场景 | CPU（一个核的百分比） | 内存 |
|---|---|---|
| 只开面板（标准 `WebView2`），空闲 | 约 1% | 宿主 133 MB，WebView2 的 6 个进程合计 171 到 176 MB |
| HTML 桌宠，动画 / 静止 | 31% / 23% | WebView2 进程合计约 180 到 220 MB |
| WPF 原生桌宠，同样的动画 | 8.8%，静止时接近 0 | 宿主 136 MB，没有 WebView2 进程 |

宿主进程的 130 MB 左右来自 WPF 本身，和是否用 WebView2 无关。面板关掉并释放 WebView2 后，那部分进程会退出。

由此确定：

- 面板用 HTML。第一次打开 0.5 到 0.8 秒，空闲时几乎不占 CPU。
- 桌宠用 WPF 原生透明窗口，按「不通过怎么办」处理。
- `WebView2CompositionControl` 要求目标框架带 Windows 版本号（至少 `net10.0-windows10.0.17763.0`），客户端现在是 19041，满足。
- `window.prompt`、`confirm` 这类浏览器原生对话框会卡住自动化，面板里的输入和确认都用页面内对话框。
- WebView2 的用户数据目录不能放在 `current\` 下，Velopack 更新时会替换这个目录。
## 参考

- [WebView2 in WPF apps（WebView2CompositionControl）](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)
- [WebView2 Runtime 分发说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [透明窗口里 WebView2 的点击问题 #997](https://github.com/MicrosoftEdge/WebView2Feedback/issues/997)