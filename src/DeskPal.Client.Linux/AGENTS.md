# Linux 客户端（GTK 4 + WebKitGTK 6.0）

和另外两个客户端共用 `Client.App`、`Client.Core`、`Shared` 和 `web/` 页面，页面约定见 `src/DeskPal.Client/web/AGENTS.md`，采集器红线见根目录 `AGENTS.md`。来源和系统集成的细节见 `docs/architecture.md` 的「Linux 客户端」。

## 编译和运行

- 任何系统都能编译：`dotnet build src/DeskPal.Client.Linux`。
- 运行要在 Linux 桌面上，系统里有 GTK 4 和 WebKitGTK 6.0（不打进安装包）。没有桌面会话的环境（例如 Multica 的后台任务）只能编译，运行效果列进交付里「需要人看的」，写明桌面环境（GNOME/KDE、X11/Wayland）。

## 宿主和采集约定

- 采集由官方插件 `LinuxOfficialProvider` 完成，新增部件加进它的部件列表；部件状态写进 `LinuxStatus`，设置页据此显示说明和按钮。
- 当前窗口按会话类型选来源（`LinuxSession`）：X11 用 Xlib，GNOME Wayland 用随包发布的 `gnome-extension/`，其他 Wayland 桌面不声明 `foreground`。改了扩展的 D-Bus 接口，扩展和 `LinuxWindowSource` 要一起改。
- 网站由随包发布的 `browser-extension/` 报给 `BrowserBridge`（只监听 127.0.0.1）。只收域名；拒绝网页来源的请求、请求体不超过 4 KB 这些检查不要放宽。
- 页面到宿主用 `window.webkit.messageHandlers.deskpal`，和 macOS 相同；拖动交给桌面（`gdk_toplevel_begin_move`）。
- Wayland 不允许程序自己定位窗口或置顶，Linux 上没有置顶小卡片、面板置顶和面板定位。不要为 Linux 补这些功能。
- 托盘是 StatusNotifierItem，没有托盘宿主时不显示图标，带 `--minimized` 启动也会打开面板。
