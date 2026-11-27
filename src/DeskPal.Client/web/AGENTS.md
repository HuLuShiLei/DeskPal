# 面板和登录页（三个平台共用）

Windows（WebView2）、macOS（WKWebView）、Linux（WebKitGTK）加载同一份页面，宿主各自负责窗口和消息转发。整体结构和消息表见 `docs/architecture.md` 的「界面」一节。

- 页面要显示的文字在 C# 里算好：加到 `PanelState`（`DeskPal.Client.Core/Ui`），由 `PanelStateBuilder` 生成，页面不要自己拼状态文案或判断隐私。改了 `PanelState` 或文案，用 `DESKPAL_UPDATE_FIXTURES=1` 运行测试，更新 `web/src/mock/fixtures/`，并同步 `web/src/bridge.ts` 的类型。
- 页面的操作发命令（`bridge.request`），在 `PanelBridge.HandleAsync`（`DeskPal.Client.App`，三个平台共用）里处理。参数名两边要一致；失败时返回当前语言的错误文字，页面直接显示。
- 页面里不要用 `window.alert / confirm / prompt`，它们会阻塞自动化检查。用 `useHost().ask`；宿主要确认时发 `dialog` 消息。
- 动态插入的元素 `autoFocus` 不生效，要用 ref 在 `useEffect` 里聚焦。对话框打开时背后的内容设为 `inert`。
- 改了页面，先 `npm run dev`（http://localhost:5190/）用模拟数据看一遍：`?mock=default`、`no-room`、`guest`、`?fail`、`?page=login`，英文加 `&lang=en`。能截图就截图检查；截不了图时，把要看的样例列进交付里「需要人看的」。之后至少在一个平台上跑真实客户端。
- 界面文案用书面中文。页面上写「房间」，例如「房间成员」「创建房间」「该房间成员看到」，不要写 Room，也不要自造简称。代码和协议里的类型名仍用 Room。写法细节见 skill `deskpal-ui-copy`。
- 所有给用户看的文字都不能写在代码里，中英文各一份：C# 生成的（`PanelState`、托盘、对话框、命令错误、Server 错误码）放 `DeskPal.Client.Core/Localization/Strings.resx`（中文）和 `Strings.en.resx`，用 `Strings.Xxx`，带参数的用 `UiLanguage.Format`；页面自己的放 `web/src/i18n/zh-CN.ts` 和 `en.ts`，用 `t("key", { 参数 })`。英文字典少键时 `npm run build` 会失败，resx 少键时 `LocalizationTests` 会失败。
- 英文通常比中文长。按钮、标签、导航、设置名称加了英文后，要在最小宽度 400px 下用 `?mock=...&lang=en` 看一遍，不能溢出或折行。英文的 fixtures 在 `web/src/mock/fixtures/en/`。
- 页面在最小宽度（400px）下不能横向溢出、设置名称不能折行；内容最宽 640px 居中。外框四周留给宿主调整窗口大小的空白，内容区偏移量跟着 `.rail`（60px）和 `.titlebar`（36px），改这两个尺寸要同时改各平台宿主（Windows 是 `PanelWindow.Host`）。
- 窗口没有系统标题栏：页面用 `ui.tsx` 的 `TitleBar`，`.titlebar` 是 `app-region: drag`，往里面放可点击的东西要包在 `app-region: no-drag` 的容器里（参考 `.caption-buttons`、`.flyout-root`），否则点不到。`window.minimize`、`window.close` 由宿主处理。WKWebView 和 WebKitGTK 不支持 `app-region`：这两个平台由页面发 `window.drag`，macOS 窗口跟随鼠标移动，Linux 交给窗口管理器（`web/src/platform.ts`），改标题栏交互时三个平台都要考虑。
- 样式按桌面程序写，不要做成网页：颜色用 `styles.css` 里的变量（浅色、深色两套），图标用 `Icon`（Windows 上是 Segoe Fluent Icons，macOS、Linux 上画 `ui.tsx` 里的 SVG，新增图标两边都要有），开关、设置行、菜单用 `ui.tsx` 里的组件。不加链接样式、手形光标、大段可选中文字；需要复制的文字加 `.selectable`。
- 元素位置固定，不随文字变化：列表行、卡片里的图标、按钮、标签不要跟在名字、标题这类长度会变的文字后面，放在固定位置（头像角标、行的右侧、固定的一行）；可选的一行内容没有时留空，不要让其他行上下移动；长文字单行省略，完整内容悬停查看（2026-11-20 用户要求，例如客户端标志放在头像右下角，首页成员卡片固定三行）。只有图标的按钮要写 `title` 和 `aria-label`。
- 置顶小卡片（页面 `page=card`）整页是 `app-region: drag`，页面收不到悬停和双击，双击由宿主窗口处理，只有 × 按钮是 `no-drag`。卡片文字同样在 C# 里算好。置顶只在内存里，切换房间或成员离开时移除。
