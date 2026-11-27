# Windows 客户端（WPF）

托盘菜单是 WPF 原生的，面板和登录页是 `web/` 下的页面，页面约定见 `web/AGENTS.md`。采集器的红线见根目录 `AGENTS.md`。

## 编译和运行

- 只能在 Windows 上运行。Linux、macOS 上可以检查能否编译：`dotnet build src/DeskPal.Client -p:EnableWindowsTargeting=true -p:SkipWebBuild=true`。
- 不在 Windows 上时，WPF 相关改动的运行效果列进交付里「需要人看的」，写明要在 Windows 上看什么。

## 宿主约定

- `RoomState`、`PresenceCoordinator`、`HubService` 的事件在后台线程触发。`PanelWindow` 通过 `Dispatcher` 合并推送 `state`，不要在事件里直接操作 WebView2。
- WPF 项目的隐式 using 不包含 `System.IO` 和 `System.Net.Http`。`System.IO` 已经在 `GlobalUsings.cs` 里补了；用 `HttpClient` 的文件要自己写 `using System.Net.Http;`。
- 面板可以拖动边框调整大小：默认 420×680，最小 400×480，不能最大化（去掉了 `WS_MAXIMIZEBOX`，没有系统边框时最大化会超出屏幕）。WebView2 会吃掉鼠标，四周留 `WindowFrame.ResizeBorder` 的空白给调整大小用，内容区下面垫一块 `--surface` 颜色，偏移量跟着页面的 `.rail` 和 `.titlebar`，改了这两个尺寸要一起改 `PanelWindow.Host`。登录窗口不能调整大小。
- 两个窗口都没有系统标题栏：构造时调用 `WindowFrame.Apply`，`OnMessage` 先交给 `WindowFrame.Handle` 处理 `window.minimize`、`window.close`。
- 新增 WPF 窗口时背景用 `WebUi.BackgroundBrush`，和页面底色一致，避免加载时闪白。
- 置顶小卡片（`MemberCards`、`MemberCardWindow`）的双击由窗口处理 `WM_NCLBUTTONDBLCLK`。
- 托盘菜单只有「打开 DeskPal」和「退出」。退出登录在设置的账号里。面板点关闭时按 `CloseAction`：每次询问、最小化到任务栏、最小化到系统托盘（隐藏，`AllowClose` 为 false），或直接退出。直接退出发 `window.quit`，和托盘菜单的「退出」走同一条路径：先停止后台服务、断开 Hub、释放单实例 Mutex。
- 通知用托盘气泡 `TaskbarIcon.ShowNotification`，由 `RoomState.Notify` 事件触发。
- lambda 的参数不要命名成 `_` 又在函数体里写 `_ = ...`，那是给参数赋值，不是丢弃返回值。
