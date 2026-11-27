# macOS 客户端（AppKit + WKWebView）

和另外两个客户端共用 `Client.App`、`Client.Core`、`Shared` 和 `web/` 页面，页面约定见 `src/DeskPal.Client/web/AGENTS.md`，采集器红线见根目录 `AGENTS.md`。和 Windows 的逐项对照见 `docs/architecture.md` 的「macOS 客户端」。

## 编译和运行

- 真正的编译只能在 Mac 上：`dotnet build src/DeskPal.Client.Mac -r osx-arm64`。.NET SDK、macos workload 和 Xcode 要配套，版本以 `ci.yml` 的 `mac` 任务的 `XCODE_VERSION`、`MACOS_WORKLOAD` 为准，不指定版本会装上要求更新 Xcode 的 workload。
- 不在 Mac 上时用 `dotnet build tools/MacCheck` 做类型检查（自动下载引用程序集）。它不检查 Info.plist、资源和链接，运行效果列进交付里「需要人看的」，写明要在 Mac 上看什么。
- 运行要在「隐私与安全性 → 辅助功能」里允许 DeskPal，否则读不到窗口标题和浏览器网址。

## 宿主约定

- `PanelController` 的行为和 Windows 的 `PanelWindow` 保持一致：200 毫秒合并推送、隐藏 5 分钟释放、15 秒没有 `ready` 或网页进程退出时重建。改一边时对照另一边。
- WKWebView 不支持 `app-region`：地址带 `platform=mac` 时页面在标题栏空白处按下鼠标发 `window.drag`，置顶卡片双击发 `window.dblclick`（`web/src/platform.ts`）。页面改了标题栏或卡片的交互，要同时看这条路径。
- 系统没有 Segoe Fluent Icons，`platform=mac` 时 `Icon` 画 `ui.tsx` 里的 SVG。新增图标要两边都有。提到 Windows、系统托盘的文案在 `i18n/index.ts` 里换成 macOS 的说法。
- 新增采集器加进 `AppDelegate.cs` 的 `AddOfficialProvider(...)`。
- 日志写 `~/Library/Logs/DeskPal`，不要写进 `.app`（会破坏签名）。本机密钥用 `FileKeyProtector`，不用钥匙串（原因见 `docs/known-issues.md`）。
