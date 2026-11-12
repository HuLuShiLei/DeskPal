# 安装时选择目录，日志放到安装目录

状态：已确认，已实现（2026-11-12）。代码未提交。

## 已定

| 主题 | 决定 |
|---|---|
| 安装向导 | Inno Setup（`installer/DeskPal.iss`），中文界面，只为当前用户安装，不需要管理员权限，不注册 Inno 自己的卸载项。内嵌 `vpk pack` 生成的 Setup.exe，选好目录后调用 `--silent --installto`。没有选用自己写的 WPF 引导程序：WPF 不能 AOT，自带运行时会让安装包多约 50 MB；Inno 只多约 2 MB |
| 默认目录 | `%LocalAppData%\Programs\DeskPal`。已经装过时默认上次的目录（读 `HKCU\...\Uninstall\DeskPal` 的 `InstallLocation`） |
| 目录检查 | 拒绝磁盘根目录、已有其他文件的目录（已有的 DeskPal 安装除外，要求 `DeskPal.exe`、`Update.exe`、`current\DeskPal.exe` 都在）、不可写的目录。静默安装跳过目录页，所以 `PrepareToInstall` 里再检查一次。换到新目录时确认一次，提示旧目录不会自动删除 |
| 日志 | 安装版写到 `<安装目录>\logs`。覆盖安装前向导先复制 `logs\`（复制失败就中止，不调用 Velopack），装完放回；Velopack 失败时也先放回再报错。Velopack 的安装日志保存为 `logs\setup.log` |
| 安装失败 | 在 `ssInstall` 调用 Velopack，失败时抛出异常，Inno 按安装失败处理，静默安装返回非 0 |
| 开机自启 | 换目录安装后，指向旧目录的 `HKCU\...\Run\DeskPal` 改到新目录的 `current\DeskPal.exe` |
| WebView2 缓存 | 安装版放在 `<安装目录>\WebView2`，卸载时一起清掉；覆盖安装会清空缓存，不影响功能 |
| 设置和规则 | `settings.json`、`personal-rules.json`、`builtin-rules.json` 仍在 `%APPDATA%\DeskPal`，卸载和重新安装都不会丢登录状态 |
| 回退 | `dotnet run` 等未安装运行，或安装目录不可写时：日志 `%APPDATA%\DeskPal\logs`，缓存 `%LOCALAPPDATA%\DeskPal\WebView2`（和以前一样） |
| 存量用户 | 已装在 `%LocalAppData%\DeskPal` 的客户端自动更新后，安装目录不变，WebView2 缓存路径也不变；日志从 `%APPDATA%\DeskPal\logs` 换到 `%LocalAppData%\DeskPal\logs`，旧日志不迁移，7 天后不再有用 |
| CI | 下载 Inno Setup 6.7.3 官方安装包（校验 SHA256）装到 Runner 临时目录，编译向导到 `InstallerOutput/`，上传到 Release，再删除 Velopack 自带的 `DeskPal-<通道>-Setup.exe`。去掉只对 MSI 生效的 `--instLocation PerUser`。`installer/` 有改动时也发测试版 |
| 不做 | MSI；为所有用户安装到 `Program Files`；把老用户自动挪到新目录 |

## 开发前验证（vpk 1.2.161，本机测试程序，已卸载并删除）

| 验证 | 结果 |
|---|---|
| Setup.exe 能否指定目录 | 能，`--installto <目录>`，没有选择目录的界面 |
| 运行时拿安装目录 | `VelopackLocator.Current.RootAppDir`；未安装时为空 |
| `Update.exe apply` | 只替换 `current\`，根目录下的其他文件保留 |
| 同一目录重新运行 Setup.exe | 整个根目录被清空 |
| `--installto` 指向有其他文件的目录 | 其中的文件被删除 |
| `--silent` | 装完不启动程序 |
| 卸载 | 删除整个根目录（延迟约 3 秒）、快捷方式和卸载项 |

## 改动

| 文件 | 改什么 |
|---|---|
| `installer/DeskPal.iss` | 新增安装向导 |
| `installer/ChineseSimplified.isl` | 新增，Inno Setup 6.7.3 的非官方简体中文语言包 |
| `src/DeskPal.Client.Core/Diagnostics/InstallDirectory.cs` | 新增：安装目录可写时返回它，否则返回 null |
| `src/DeskPal.Client/Services/AppPaths.cs` | 新增：日志目录、WebView2 目录 |
| `src/DeskPal.Client/App.xaml.cs` | 日志目录改用 `AppPaths.LogDirectory` |
| `src/DeskPal.Client/Ui/WebUi.cs` | WebView2 目录改用 `AppPaths.WebView2Directory` |
| `tests/DeskPal.Tests/Diagnostics/InstallDirectoryTests.cs` | 新增 |
| `.github/workflows/ci.yml` | 见「已定」CI 一行；Release 说明加一句「安装时可以选择安装目录」 |
| `.gitignore` | `installer/Output/`、`InstallerOutput/` |
| 文档 | `progress.md`、`architecture.md`、`known-issues.md`、`roadmap.md`、`AGENTS.md`、`deploy/README.md` |

## 自测（本机，Inno Setup 6.7.3，版本号 0.9.1 / 0.9.2 的本地包）

| 项 | 结果 |
|---|---|
| `dotnet build`、`dotnet test` | 通过，102 个测试 |
| 静默安装到已有其他文件的目录 | 拒绝，原有文件保留 |
| 静默全新安装到带空格的路径 | 通过；卸载项指向所选目录 |
| 启动安装版 | 日志在 `<安装目录>\logs\deskpal.log`，缓存在 `<安装目录>\WebView2`，登录状态从 `%APPDATA%\DeskPal` 恢复，`%APPDATA%\DeskPal\logs` 没有新写入 |
| DeskPal 运行中覆盖安装同一目录 | 通过；Velopack 先结束进程，旧日志保留，新增 `setup.log` |
| `Update.exe apply` 到 0.9.2 | 通过；`logs\` 保留并继续追加 |
| 向导界面 | 中文正常；默认填上次的目录；换目录时弹出确认；完成页「启动 DeskPal」从新目录启动 |
| 卸载 | 安装目录、快捷方式、卸载项都清掉 |
| 审查后回归：有 `Update.exe` 和 `current\` 但不是 DeskPal 的目录 | 拒绝，原有文件保留 |
| 审查后回归：Velopack 返回失败（用 `where.exe` 代替） | 静默安装退出码 3，目录回滚 |
| 审查后回归：覆盖安装、换目录安装 | 旧日志保留；开机自启从旧目录改到新目录 |
| CI | 未测，推送 main 后看 Actions 和 Release |
| 没有 WebView2 Runtime 的系统 | 未测，和以前一样 |