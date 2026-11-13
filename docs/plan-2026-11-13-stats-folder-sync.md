# 使用统计通过同步文件夹在电脑之间同步

方案按 2026-11-13 的确认更新（和服务器同步二选一、沿用 8 位 PIN 码、停止时不删除文件、开启时不能清除）。用户说「开始开发」后才改业务代码。服务器同步见 `docs/plan-2026-11-13-stats-sync.md`（代码已在开发中、未提交），两者都做，用户只能开启其中一种。

## 这次做什么

1. 统计页「数据和隐私」里，在「导入导出」下面增加设置行「同步文件夹」。用户选一个本机文件夹，这个文件夹由用户自己的网盘或同步软件（OneDrive、坚果云、Dropbox、Syncthing、NAS 共享目录等）在电脑之间同步。
2. DeskPal 只读写这个文件夹：每台电脑定时把自己记录的使用统计写成加密文件，同时读取其他电脑写的文件并合并进本机统计库。服务器不参与，不保存任何使用统计。
3. 文件用 PIN 码加密后再写入，网盘服务商看到的只是密文。
4. 停止同步只停止读写，不删除文件夹里的文件（文件夹属于用户，其他电脑可能还在用）。
5. 和「同步到服务器」二选一：一种开启时，另一种的设置行不可操作，说明行提示先关闭已开启的那一种。
6. 同步文件夹开启时不能「清除」使用统计，点击后提示先停止同步。

## 为什么考虑这条路线

| | 服务器同步 | 同步文件夹 |
|---|---|---|
| 服务器是否保存数据 | 保存密文，硬约束 4 要放宽 | 不保存，硬约束不变，`usage.note` 原句仍然成立 |
| 能拿到密文的人 | 能拿到服务器数据库或备份的人 | 用户自己选的网盘服务商 |
| 用户门槛 | 打开开关，输入 PIN 码 | 需要已经在用某种网盘或同步工具 |
| 服务器容量、上限 | 每个账号 100 MB 上限 | 不涉及，由用户的网盘决定 |
| 开发量 | Server、迁移、客户端、集成测试 | 只改客户端 |

## 已定（2026-11-13）

| 主题 | 决定 |
|---|---|
| 范围 | 按「账号 + 这台电脑」：文件夹路径存在本机 `settings.json`，按 `userId` 区分。每台电脑各自选择文件夹（不同电脑上网盘目录的路径不同）。换账号后读写各自的目录 |
| 目录结构 | `<所选文件夹>\DeskPal\<userId>\key.json`（盐、参数、校验值），`<所选文件夹>\DeskPal\<userId>\<deviceId>\yyyy-MM-dd.dps`（这台电脑这一天的加密内容）。同一个网盘里可以放多个账号，互不读取 |
| 只写自己 | 每台电脑只写自己 `deviceId` 目录下的文件，从不修改其他电脑的文件。两台电脑不会同时写同一个文件，网盘不会产生冲突副本 |
| 和服务器同步的关系 | 二选一。两套合并口径不同（服务器是追加、文件夹是整份替换），同时开启会互相干扰。一种开启时，另一种的开关或按钮不可用；PanelBridge 收到另一种的开启命令时也拒绝 |
| 加密 | 复用 StatsSyncCrypto：8 位数字 PIN 码，Argon2id 派生密钥（参数随盐保存），每天的内容压缩后用 AES-GCM 加密。key.json 只有盐、参数和校验值，没有 PIN 码和密钥。不提供不加密的选项 |
| 第一台电脑 | 选择文件夹时，目录下没有 `key.json`：设置 PIN 码、再输入一次确认，生成 `key.json` 后开始同步 |
| 其他电脑 | 选择同一个网盘目录时，已经有 `key.json`：输入 PIN 码，用校验值判断是否正确。正确后开始同步 |
| 本机保存密钥 | 和服务器同步相同：DPAPI（当前用户）加密后存在 `settings.json`，按账号区分，自动恢复登录不再输入。退出登录时先同步一次，再整份删除（路径和密钥），重新登录后再次选择文件夹 |
| 写入内容 | 只写这台电脑记录的数据：`Segments` 和 `DailyRollups` 中 `DeviceId` 等于本机的记录，按本地日期分文件。查看记录不写 |
| 写入时机 | 开始同步时写全部日期；之后每 10 分钟写有变化的日期（按每天内容的哈希判断），退出时再写一次 |
| 写入方式 | 先写同目录的 `.tmp` 文件，再替换正式文件，避免网盘同步到写了一半的文件 |
| 读取时机 | 开始同步时、登录后、每 10 分钟各读一次。不用 `FileSystemWatcher`，网盘目录的变更通知不可靠 |
| 读取方式 | 遍历其他 `deviceId` 目录，只处理名称严格符合 `yyyy-MM-dd.dps` 的文件，忽略网盘生成的冲突副本和 `.tmp`。按文件修改时间和大小判断是否变化，只读取变化的文件 |
| 合并口径 | 读取某台电脑某一天的文件时，**先删除本机库里这台电脑这一天的全部片段和汇总，再整份换成文件内容**。不复用手动导入的 `INSERT OR IGNORE`，原因见「风险点」第 1 条。手动导入的行为不变 |
| 读取失败 | 解密失败、解压失败、格式不对（网盘还没同步完整）时跳过这个文件，下一轮再试，不提示 |
| 保留 | 每台电脑按各自的保留时长折叠自己的片段；折叠后这一天的内容变化，下一轮重写文件，其他电脑读到后标题和时间轴一起消失。其他电脑不折叠别人的数据，跟着文件走 |
| 文件夹不可用 | 网盘未挂载、目录被删、没有写入权限时，这一轮跳过，设置行说明显示「文件夹暂时无法访问」。恢复后自动继续 |
| 公开使用统计 | 不变。房间成员查看时仍由本人的客户端回复，回复内容包含合并进来的其他电脑的数据（和导入后的行为一致） |
| 停止 | 只停止读写，删除本机密钥和读写状态，不删除文件夹里的任何文件。对话框里说明如何手动删除 |
| 清除全部统计 | 同步文件夹开启时，「清除」按钮可以点，点击后不弹清除对话框，提示「请先停止同步文件夹，再清除使用统计」。PanelBridge 收到 stats.clear 时同样拒绝。停止后清除只影响本机；再次开启时会把文件夹里的数据合并回来，停止对话框里已说明 |
| 统计页说明 | 不需要替换 usage.note，服务器仍然不保存使用统计 |

## 界面

设置行「同步文件夹」，有三种状态：

| 状态 | 说明行 | 操作 |
|---|---|---|
| 未开启 | 加密写入所选文件夹，由网盘等同步工具同步到其他电脑 | 按钮「选择文件夹」→ 系统文件夹选择对话框 → 说明对话框 → 设置或输入 PIN 码 |
| 已开启 | 显示文件夹路径（`.selectable`，过长时省略中间） | 按钮「停止」→ 停止对话框 |
| 已开启，文件夹无法访问 | 文件夹暂时无法访问 | 同上 |
| 已开启同步到服务器 | 已开启同步到服务器。如需使用，请先关闭 | 「选择文件夹」不可用 |

「同步到服务器」那一行同样增加一种状态：同步文件夹开启时，开关不可用，说明行为「已开启同步文件夹。如需使用，请先关闭」。

文件夹选择用 WPF 自带的 `Microsoft.Win32.OpenFolderDialog`，和导出用的 `SaveFileDialog` 一样在 `PanelBridge` 里弹出。PIN 码输入框沿用服务器同步方案里扩展的掩码、只接受数字、固定长度。

## 文案草稿

按 `deskpal-ui-copy`。只新增键，不改已有文案。

| 位置 | 中文 | English |
|---|---|---|
| 设置行标题 | 同步文件夹 | Sync folder |
| 设置行说明（问号） | 加密写入所选文件夹，由网盘等同步工具同步到其他电脑 | Write an encrypted copy to a folder that your cloud drive or sync app shares with your other computers |
| 设置行说明（已开启） | 文件夹路径 | 同左 |
| 设置行说明（无法访问） | 文件夹暂时无法访问 | The folder can't be reached |
| 设置行说明（服务器同步已开启） | 同步到服务器已开启，需先关闭 | Server sync is on. Turn it off first. |
| 按钮 | 选择文件夹 / 停止 | Choose folder / Stop |
| 文件夹选择对话框标题 | 选择同步文件夹 | Choose a sync folder |
| 说明对话框：标题 | 同步文件夹 | Sync folder |
| 说明对话框：正文（新文件夹） | 使用统计将用 8 位 PIN 码加密后写入该文件夹，包括窗口标题、网页标题和域名。该文件夹由你使用的网盘或同步工具同步到其他电脑，服务器不保存这些数据。\n\n在其他电脑上选择同一文件夹并输入 PIN 码后即可同步。PIN 码无法找回，请妥善保存。 | Your usage stats, including window titles, page titles and domains, will be encrypted with an 8-digit PIN and written to this folder. Your cloud drive or sync app copies it to your other computers. The server doesn't store this data.\n\nTo sync another computer, choose the same folder there and enter the PIN. The PIN can't be recovered. Keep it somewhere safe. |
| 说明对话框：按钮 | 下一步 | Next |
| 设置 PIN 码 / 确认 PIN 码 / 不一致 / 错误 | 复用服务器同步的键 | 同左 |
| 输入 PIN 码：标题 | 输入 PIN 码 | Enter PIN |
| 输入 PIN 码：正文（已有同步数据） | 该文件夹已由其他电脑开启同步。输入在那台电脑上设置的 8 位 PIN 码后，本机的使用统计也将加密写入该文件夹，包括窗口标题、网页标题和域名。\n\n如忘记 PIN 码，可删除该文件夹中的「DeskPal」目录后重新选择，各电脑上的本机数据保留。 | Another computer already syncs through this folder. Enter the 8-digit PIN set there. Usage stats from this computer, including window titles, page titles and domains, will then be encrypted and written to it too.\n\nIf you forgot the PIN, delete the "DeskPal" folder inside it and choose the folder again. Data on each computer is kept. |
| 输入 PIN 码：按钮 | 开启同步 | Turn on |
| 开启完成 | 已开启同步文件夹 | Sync folder is on |
| 停止对话框：标题 | 停止同步 | Stop syncing |
| 停止对话框：正文 | 本机将不再读写该文件夹，本机数据和文件夹中的文件均保留。如需删除文件，请手动删除该文件夹中的「DeskPal」目录。 | This computer will stop reading and writing the folder. Data on this computer and files in the folder are kept. To remove the files, delete the "DeskPal" folder inside it. |
| 停止对话框：按钮 | 停止 | Stop |
| 停止完成 | 已停止同步 | Syncing stopped |
| 文件夹不可写 | 无法写入该文件夹，请选择其他文件夹。 | Can't write to this folder. Choose another one. |
| 服务器同步已开启时选择文件夹 | 同步到服务器已开启，请先关闭。 | Server sync is on. Turn it off first. |
| 统计页说明（同步文件夹开启时） | 数据保存至本地，并用 PIN 码加密后写入同步文件夹。传输时服务器将加密转发，您的数据不会保存至服务器。 | Data is stored locally and written to the sync folder, encrypted with your PIN. During transfer, the server encrypts and forwards it. Your data is not stored on the server. |
| 开启时点击「清除」 | 同步文件夹开启时无法清除，请先停止同步。 | Stop the sync folder before clearing usage stats. |
| CHANGELOG | 使用统计也可以通过网盘等同步工具在电脑之间同步：选择一个同步文件夹，文件用 PIN 码加密，服务器不保存这些数据 | Usage stats can also be synced through a cloud drive or sync app. Choose a sync folder; the files are encrypted with a PIN and the server doesn't store them |

「Sync folder」「Choose folder」在 400px 下的宽度实现后在浏览器里核对。

## 改动清单

| 项目 | 文件 | 改什么 |
|---|---|---|
| Client.Core | `Usage/SqliteUsageStore.cs` | 按「设备 + 日期」导出本机记录；按「设备 + 日期」整份替换（一个事务内先删后插）；本机写入状态表 `FolderSyncDays`（日期、哈希）和读取状态表 `FolderSyncFiles`（设备、日期、修改时间、大小），清除时一起清空 |
| Client.Core | `Usage/FolderSync.cs`（新） | 纯逻辑：目录和文件名规则、`key.json` 读写、决定这一轮写哪些日期、读哪些文件。文件系统通过接口传入，便于测试 |
| Client.Core | `Usage/StatsSyncCrypto.cs` | 直接复用，不改 |
| Client | `Services/FolderSyncService.cs`（新） | 定时读写、退出时写入、开启（设置或输入 PIN 码）、停止、文件夹不可用时的状态 |
| Client | `FolderSyncService.cs` | 路径、盐和 DPAPI 加密的密钥存在 `usage\<userId>.folder-sync`（和服务器同步的 `.sync-key` 并列），不放 `settings.json` |
| Client | 退出登录 | 先同步一次（最多 5 秒），再删除路径和密钥 |
| Client | `Ui/PanelBridge.cs` | 命令 `stats.folder.choose`（弹出文件夹选择，返回是新建还是已有 `key.json`）、`stats.folder.enable`（`pin`）、`stats.folder.stop`；`stats.clear` 在同步文件夹开启时返回错误；两种同步的开启命令互相检查 |
| Client.Core | `Ui/PanelState.cs`、`PanelStateBuilder.cs` | `UsageView` 增加 `folderSync`：`enabled`、`path`、`unavailable`；两行的说明按另一种是否开启切换 |
| web | `pages/StatsPage.tsx`、`bridge.ts`、`mockHost.ts`、`i18n/*` | 设置行、说明对话框、停止对话框；模拟数据三种状态 |
| Localization | `Strings.resx`、`Strings.en.resx` | 文件夹选择对话框标题、不可写错误 |
| Tests | `Usage/FolderSyncTests`、`SqliteUsageStoreTests` | 见自测矩阵 |
| 文档 | `architecture.md`、`progress.md`、`known-issues.md`、`CHANGELOG.md` | 使用统计口径增加「同步文件夹」 |

## 不改什么

- 不改硬约束 4，Server 和数据库都不改。
- 不改手动导入导出的文件格式和 `INSERT OR IGNORE` 口径。
- 不改「公开使用统计」的流程和 `usage.note`。
- 不同步查看记录和 `settings.json` 里的其他设置。

## 自测矩阵

| 项 | 怎么验证 |
|---|---|
| 未开启时不读写任何文件 | `FolderSyncTests`（内存文件系统） |
| 只写本机 `deviceId` 的文件，只写有变化的日期 | `FolderSyncTests` |
| 两台电脑（两个库 + 同一个内存目录）交替同步，双方数据一致，重复读取不翻倍 | `FolderSyncTests` |
| 另一台电脑正在记录的片段，结束时间变长后再次读取，本机跟着变长 | `FolderSyncTests` |
| 另一台电脑折叠过期片段后，本机读取不翻倍，标题消失 | `FolderSyncTests` |
| 冲突副本、`.tmp`、解密失败的文件被跳过 | `FolderSyncTests` |
| PIN 码错误不保存密钥；正确时保存，之后不再要求输入 | `FolderSyncTests` + `StatsSyncCryptoTests` |
| 文件夹里的文件不是明文 | `FolderSyncTests` |
| 文件夹不可用时跳过并显示状态，恢复后继续 | `FolderSyncTests` |
| 两个账号用同一个网盘目录，互不读取 | FolderSyncTests |
| 停止后文件夹里的文件都还在，本机不再读写 | FolderSyncTests |
| 同步文件夹开启时 stats.clear 被拒绝，本机数据不变 | PanelBridge 相关测试或端到端自检 |
| 一种同步开启时，另一种的开启命令被拒绝 | 单元测试 + 浏览器模拟数据 |
| 中英文三种状态和对话框，400px 下不折行 | 浏览器模拟数据（`?mock=stats`、`&lang=en`） |
| 真实客户端：两个本机目录模拟两台电脑（两个 `deviceId`），开启、合并、停止 | 端到端自检（`deskpal-e2e-check`） |

## 风险点

1. **手动导入的口径不能直接复用。** `Import` 用 `INSERT OR IGNORE`：另一台电脑正在记录的片段第一次读到时结束时间较短，之后变长也不会更新；那台电脑折叠片段后，本机会同时有它的片段和汇总，统计翻倍。所以按「设备 + 日期」整份替换：每台电脑是自己数据的唯一来源。
2. **跨时区。** 文件按写入方电脑的本地日期划分，替换时按本机时区删除同一天，两台电脑时区不同时边界会错开几个小时。`known-issues.md` 记一条，暂不处理。
3. **网盘延迟和部分同步。** 网盘可能先同步一部分文件，或文件还没下载完（OneDrive「仅在线」占位文件）。解密失败就跳过，下一轮重试；占位文件读取时会触发下载，可能较慢，读取放在后台线程。
4. **PIN 码丢失。** 只影响「只在别的电脑上、还没合并过来的那部分」，本机数据不受影响。
5. **用户门槛。** 没有网盘的用户用不上，这是这条路线的前提。