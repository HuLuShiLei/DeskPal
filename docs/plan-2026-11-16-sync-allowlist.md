# 去掉保留时长，同步到服务器改为名单内账号可用

状态：2026-11-16 用户确认，已实现。和本文不同的地方记在 [progress.md](progress.md) 的变更日志。

## 这次做什么

1. 去掉「保留时长」设置和按天折叠：详细记录（时间轴、具体时刻、标题）一直保留。
2. 「同步到服务器」只对名单内的账号显示。名单用 Stack 环境变量配置，写法参照管理员（`DESKPAL_ADMIN_USER`）。名单内账号开启时仍由用户自己设置 8 位 PIN 码，加密方式不变。
3. 名单外的账号：「同步到服务器」那一行不显示；已经开启的，停止同步并删除服务器上的副本。
4. 去掉服务器上的大小上限：合计 100 MB，以及单日 2 MB（2026-11-16 实现后再去掉单日上限）。
5. 去掉导入文件的 100 MB 上限。
6. 「同步文件夹」不变，所有账号都可用；和「同步到服务器」仍只能开启一种。

## 已定（2026-11-16 用户确认）

| 主题 | 决定 |
|---|---|
| 名单内开启方式 | 显示「同步到服务器」，开启仍由用户设置 PIN 码，不自动弹窗，不改加密 |
| 名单外已开启的账号 | 停止同步，删除服务器上的副本（每天的密文、统计设置、盐和校验值）。本机数据保留 |
| 两种同步 | 仍只能开启一种 |
| 导入上限 | 去掉 |
| 已经折叠的旧日期 | 恢复不了，保持按天汇总；以后不再折叠 |

## 改动清单

| 项目 | 文件 | 改什么 |
|---|---|---|
| Server | `Features/Stats/StatsSyncOptions.cs` | 删 `MaxTotalMegabytes` 和 `MaxDayMegabytes`；加 `AllowedUserNames`（逗号分隔的用户名，不区分大小写）和 `IsAllowed(userName)` |
| Server | `Features/Stats/StatsSyncService.cs` | 去掉合计上限和单日上限检查。名单外账号：`GetStatusAsync` 返回 `Enabled = false`、`Allowed = false`；开启、上传一天、上传设置返回 403 `statsSync.notAllowed`；下载返回 404。新增 `DisableUnlistedAsync`：把名单外、仍开启的账号逐个调用现有的 `DisableAsync` |
| Server | `Program.cs` | 启动迁移之后调用 `DisableUnlistedAsync`，记一条日志（只记关闭了几个账号，不记用户名） |
| Server | `appsettings.json` | `StatsSync` 节去掉 `MaxTotalMegabytes`（如有），加 `AllowedUserNames: ""` |
| Shared | `Api/StatsSyncDtos.cs` | `StatsSyncStatusDto` 末尾加 `bool Allowed = false` |
| Shared | `Api/ApiErrorCodes.cs` | 加 `statsSync.notAllowed`；删 `statsSync.quotaExceeded` 和 `statsSync.dayTooLarge` |
| deploy | `stack.yml`、`README.md` | 加 `StatsSync__AllowedUserNames: ${DESKPAL_STATS_SYNC_USERS:-}`，注释说明用逗号分隔多个用户名；不设置时所有账号都看不到同步到服务器 |
| Client.Core | `Usage/StatsSync.cs` | 记录最近一次状态里的 `Allowed`（启动后、第一次取到状态之前按不可用处理）。`SyncedStatsSettings` 去掉 `RetentionDays` |
| Client.Core | `Usage/SqliteUsageStore.cs` | 新增 `PruneViews(now)`，只删 7 天前的查看记录。`Compact` 保留（测试里模拟旧版本和其他电脑折叠过的日期） |
| Client.Core | `Usage/UsageExport.cs` | 删 `MaxFileBytes` |
| Client.Core | `Ui/PanelState.cs`、`PanelStateBuilder.cs` | `UsageView` 去掉 `RetentionDays`、`RetentionOptions`；删 `RetentionOptions`；`PanelInput.UsageRetentionDays` 改成 `ShowUsage`（原来用 null 隐藏统计区）；`Sync` 只在 `Allowed` 时给出，否则为 null |
| Client.Core | `Localization/Strings*.resx` | 删 `Error_Retention`、`Import_TooLarge`、`ApiError_statsSync_quotaExceeded`；加 `ApiError_statsSync_notAllowed` |
| Client | `Services/UsageService.cs` | 删 `SetRetention` 和定时折叠，定时器改为只调用 `PruneViews`；导入后不再折叠；删导入文件大小检查 |
| Client | `Services/AppSettings.cs` | 删 `UsageRetentionDays`（旧 `settings.json` 里的这个键读入时忽略） |
| Client | `Services/StatsSyncService.cs` | `SettingsSource` 不再读写保留时长；对外给出 `Allowed` |
| Client | `Ui/PanelBridge.cs` | 删 `stats.retention` 命令；`ShowUsage`、`SyncAllowed` 传给 `PanelStateBuilder` |
| web | `pages/StatsPage.tsx`、`bridge.ts`、`mockHost.ts`、`i18n/*` | 删保留时长那一行和 `stats.retention`；删 `usage.retention*`；改 `sync.settingsSynced`（去掉「保留时长」）；模拟的同步错误改为一条不提大小上限的失败提示 |
| web | `mock/fixtures/**` | 用 `DESKPAL_UPDATE_FIXTURES=1` 重新生成 |
| Tests | `Usage/StatsSyncTests.cs`、`Server/StatsSyncEndpointTests.cs`、`Ui/PanelSamples.cs` 等 | 见验收对照 |
| 文档 | `AGENTS.md` 硬约束 4、`docs/architecture.md`、`docs/progress.md`、`docs/known-issues.md`、`CHANGELOG.md` | 见下方「文档」 |

## 行为口径

**保留时长**

- 统计页不再有「保留时长」一行，`stats.retention` 命令删除。
- 本机不再把片段折成按天汇总。已经折过的日期保持按天汇总，没有时间轴。
- 查看记录（谁看过我的统计）仍只保留 7 天。
- 从其他电脑同步过来的按天汇总照常读取、照常显示。

**名单**

- 环境变量 `DESKPAL_STATS_SYNC_USERS`，逗号分隔，例如 `alice,bob`，前后空格忽略，不区分大小写，和管理员一样按用户名匹配。不设置时名单为空，所有账号都不可用。
- 名单在 Server 启动时读取，改名单要重建容器（Portainer 改 Stack 环境变量后重新部署）。
- 客户端每次同步（启动、每 10 分钟、退出）都会读取状态，`Allowed` 跟着更新；还没取到状态时不显示这一行。

**名单外的账号**

- 统计页不显示「同步到服务器」。「同步文件夹」照常显示、可以开启。
- Server 启动时：名单外、仍开启同步的账号，在一个事务里关闭并删除每天的密文、统计设置、盐和校验值（复用 `DisableAsync`）。
- 客户端下一次同步看到 `Enabled = false`，按现有逻辑删除本机密钥，回到关闭。本机统计数据不动。不弹提示，`CHANGELOG.md` 写明。
- 旧版本客户端：看到的仍是「同步到服务器」开关；打开后 Server 返回 403，旧版本没有这个错误码的文案，显示通用的错误提示。

**名单内的账号**

- 和现在完全一样：开关、设置 PIN 码、输入 PIN 码、关闭并删除。
- 服务器上不检查单日大小，也不限制合计大小。

**跟随账号的统计设置**

- 只剩「不记录标题」。新客户端上传的设置不再带保留时长；读到旧客户端上传的（带 `retentionDays`）时忽略这一项。旧客户端读到新客户端上传的设置时，保留时长读成 0，不在可选值里，按现有逻辑忽略，不会改它本机的保留时长。

**导入**

- 不再检查文件大小。文件结构不对、版本不支持仍提示失败。

## 不改什么

- 不改 PIN 码、Argon2id、AES-GCM 加密，也不改接口路径。
- 不改「同步文件夹」的读写逻辑和文案。
- 不改「公开使用统计」和房间成员查看。
- 单日 2 MB 上限已在同日去掉，不再保留。
- 不改导出文件格式（`UsageExport` 版本 1）。
- 不删 Server 数据库字段，不加迁移。名单只在配置里，不写数据库。
- 不改管理员和注册邀请码。

## 验收对照

| 验收 | 怎么验证 |
|---|---|
| 统计页没有保留时长；`stats.retention` 不再处理 | 浏览器模拟数据（`?mock=stats`、`&lang=en`）+ fixtures 重新生成 |
| 超过 90 天的片段不再被折叠，查看记录仍只留 7 天 | `UsageTests`：`PruneViews` 只删查看记录 |
| 超过原来的单日 2 MB 仍能上传 | `StatsSyncEndpointTests` |
| 名单外：状态 `Allowed = false`；开启、上传、上传设置返回 403 `statsSync.notAllowed`；下载 404 | `StatsSyncEndpointTests` |
| 名单不区分大小写、忽略空格、逗号分隔多个 | `StatsSyncOptions` 单元测试 |
| 启动时名单外已开启的账号被关闭，数据删除；名单内的不动 | Server 集成测试（调用 `DisableUnlistedAsync`） |
| 客户端：`Allowed = false` 时 `UsageView.Sync` 为 null；`Allowed = true` 时和现在一样 | `PanelStateBuilder` 测试 |
| 名单外的客户端已开启同步：下一次同步删除本机密钥，回到关闭，本机数据不变 | `StatsSyncTests`（模拟 Server） |
| 统计设置只同步「不记录标题」；读到带 `retentionDays` 的旧设置不报错 | `StatsSyncTests` |
| 导入超过 100 MB 的文件不再被拒绝 | 代码审查（删掉检查即可；不在测试里造 100 MB 文件） |
| 新错误码有中英文文案 | `LocalizationTests` |
| 真实客户端：名单内能看到并开启；名单外看不到，已开启的被关闭 | 端到端自检（`deskpal-e2e-check`，本地 Server 用环境变量切换名单） |

## 文档

- `AGENTS.md` 硬约束 4：统计设置只剩「不记录标题」；同步到服务器只对名单内账号可用。
- `docs/architecture.md`：统计存储（不再折叠）、同步到服务器（名单、没有合计上限）、导入（没有上限）、`settings.json` 字段、页面命令列表。
- `docs/progress.md`：「使用统计」「同步到服务器」「跟随账号的设置」三行改口径，加变更日志。
- `docs/known-issues.md`：删掉「各电脑的保留时长不同时」，改写成「旧版本客户端仍按保留时长折叠，同一账号混用时会把其他电脑的片段也折掉」；加两条：本机统计库和导出文件会一直变大；导入大文件整个读进内存。
- `CHANGELOG.md`：去掉保留时长；同步到服务器暂时只对部分账号开放，其他账号已开启的同步已关闭、服务器上的数据已删除；导入不再限制文件大小。
- 文案按 `deskpal-ui-copy`。

## 风险点

1. **名单外账号的服务器数据会被直接删除，而且没有提示。** 本机数据还在，只丢「只在其他电脑上、还没同步到本机」的那部分。管理员把某人从名单里删掉、重启 Server 后同样会删除。
2. **新旧客户端混用。** 旧版本客户端仍每 6 小时按保留时长折叠，而且折的是本机库里所有电脑的片段；它上传后，新客户端会用折过的版本替换「那台电脑」的记录。新客户端自己记录的不受影响。
3. **本机库和导出文件只增不减。** 查询按日期走索引，统计页不会变慢；但导出、导入是整库操作，导入时整个文件读进内存，几百 MB 的文件会占用相应内存。
4. **服务器容量没有上限。** 名单内账号的密文一直增长，只能靠名单控制人数。
5. **`Allowed` 默认 false。** 新客户端连到还没更新的 Server 时看不到同步到服务器。推送 main 时 Server 先自动部署，客户端测试版随后发布，正常情况下不会出现。