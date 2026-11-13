# 使用统计同步到服务器

方案已按 2026-11-13 的确认更新（加密改为独立的 8 位 PIN 码，派生算法用 Argon2id）。已实现，实现时和本方案不同的地方记在 [progress.md](progress.md) 的「偏离方案」。

## 这次做什么

1. 统计页「数据和隐私」里，在「导入导出」下面增加开关「同步到服务器」，默认关闭，按账号生效。
2. 开启时用户设置一个 8 位数字 PIN 码，与登录密码无关。统计数据用 PIN 码加密后上传，服务器只保存密文。之后定时上传有变化的日期；在其他电脑登录并输入 PIN 码后，下载并合并。
3. PIN 码不提供找回，也不提供修改。忘记时只能关闭同步（服务器上的数据随之删除），再用新 PIN 码重新开启。
4. 关闭时立即删除服务器上该账号的全部统计数据。删除成功才算关闭；服务器连不上时开关保持开启并提示。
5. 文案按项目 skill `deskpal-ui-copy` 写（本次已新建并登记到 `AGENTS.md`）。

## 已定（2026-11-13）

| 主题 | 决定 |
|---|---|
| 范围 | 按账号：开关状态存在服务器的 `Users` 上。一台电脑开启后，其他电脑登录时看到「已开启」，输入 PIN 码后开始同步 |
| 加密 | 独立 8 位数字 PIN 码，不和登录密码耦合，不提供找回和修改。客户端用 Argon2id（64 MB 内存、3 次迭代，每个账号一个随机盐，参数随密文保存，以后可以调高）派生密钥，本机实测派生一次约 0.13 秒，每天的数据压缩后用 AES-GCM 加密。服务器保存盐和一段用密钥加密的校验值，用来在客户端判断 PIN 码是否正确，服务器不接触 PIN 码和密钥 |
| 本机保存密钥 | 输入正确后，密钥用 Windows DPAPI（当前用户）加密存在 `settings.json`，按账号区分。之后自动恢复登录不再输入。退出登录时删除本机密钥 |
| 上传内容 | 本机统计库里的片段（`Segments`）和按天汇总（`DailyRollups`），和导出文件内容相同。「不记录标题」命中的记录本来就没有标题。查看记录（谁看过我的统计）不上传 |
| 上传时机 | 开启时上传全部；之后每 10 分钟上传有变化的日期，退出时再上传一次。导入、改保留时长后，变化的日期在下一次上传 |
| 下载时机 | 登录（包括自动恢复登录）后、或刚输入 PIN 码后，第一次上传之前先下载服务器上比本机新的日期，按电脑和日期整份替换其他电脑的记录（`SqliteUsageStore.MergeDay`），本机自己的记录不动，不翻倍。下载失败时这一轮不上传 |
| 存储单位 | 按天：一个账号一天一条，内容是这一天所有电脑的片段和汇总。服务器不解析内容 |
| 保留 | 服务器跟着客户端：本机过期折成按天汇总后，这一天重新上传，服务器上的标题和时间轴一起消失 |
| 上限 | 每个账号服务器上最多 100 MB（和导入上限相同），单天最多 2 MB。超过时提示，不自动删旧数据 |
| 关闭 | 确认后调用删除接口：服务器在一个事务里把开关设为关闭、删除该账号的全部统计数据、盐和校验值。成功后各电脑的本机密钥在下次同步时发现已关闭并删除。失败时开关保持开启 |
| 忘记 PIN 码 | 输入 PIN 码的对话框里说明：关闭同步后可以重新开启，服务器上的数据会被删除。本机数据不受影响 |
| 清除全部统计 | 同步开启时不能清除（2026-11-13 改，和同步文件夹统一）。「清除」按钮可以点，点击后不弹清除对话框，提示「请先关闭同步，再清除使用统计」；`PanelBridge` 收到 `stats.clear` 时同样拒绝。关闭同步时服务器上的数据已经删除，之后清除只影响本机 |
| 退出登录、换账号 | 不删除服务器数据；本机按 `userId` 分库，不受影响 |
| 公开使用统计 | 不变。房间成员查看时仍由本人的客户端回复，不读服务器上的副本，离线仍看不到 |
| 统计页说明 | `usage.note` 原句不改，同步开启时换成另一句 |

## 界面

设置行「同步到服务器」右侧是开关，有三种状态：

| 状态 | 开关 | 说明行 | 操作 |
|---|---|---|---|
| 未开启 | 关 | 加密保存到服务器，在其他电脑上登录后同步 | 打开开关 → 开启对话框 → 确认 PIN 码对话框 |
| 已开启，本机有密钥 | 开 | 同上 | 关闭开关 → 关闭对话框 |
| 已开启，本机没有密钥（其他电脑开启的） | 开 | 输入 PIN 码后在这台电脑上同步 | 行内按钮「输入 PIN 码」；关闭开关同样走关闭对话框 |

不自动弹出输入 PIN 码的对话框。

对话框的输入框扩展三个选项：掩码显示、只接受数字、固定长度 8 位（不满 8 位时确认按钮不可用）。

## 文案

按 `deskpal-ui-copy`。新增键，不改已有文案。

| 位置 | 中文 | English |
|---|---|---|
| 设置行标题 | 同步到服务器 | Server sync |
| 设置行说明（问号） | 加密保存到服务器，在其他电脑上登录后同步 | Store an encrypted copy on the server and sync it to your other computers |
| 设置行说明（本机没有密钥） | 已在其他电脑开启，输入 PIN 码后在本机同步 | Turned on from another computer. Enter the PIN to sync here. |
| 设置行说明（同步文件夹已开启） | 同步文件夹已开启，需先停止 | The sync folder is on. Stop it first. |
| 行内按钮 | 输入 PIN 码 | Enter PIN |
| 开启：对话框标题 | 同步到服务器 | Server sync |
| 开启：对话框正文 | 使用统计将用 8 位 PIN 码加密后保存到服务器，包括窗口标题、网页标题和域名。在其他电脑上登录并输入 PIN 码后即可同步。\n\nPIN 码无法找回，请妥善保存。关闭同步时，服务器上的数据将立即删除，本机数据保留。 | Your usage stats, including window titles, page titles and domains, will be encrypted with an 8-digit PIN and stored on the server. To sync another computer, sign in there and enter the PIN.\n\nThe PIN can't be recovered. Keep it somewhere safe. Turning sync off deletes the server copy immediately. Data on this computer is kept. |
| 开启：输入框占位 | 设置 8 位数字 PIN 码 | Set an 8-digit PIN |
| 开启：按钮 | 下一步 | Next |
| 确认 PIN 码：标题 / 正文 / 按钮 | 确认 PIN 码 / 再次输入 PIN 码。 / 开启同步 | Confirm PIN / Enter the PIN again. / Turn on |
| 两次不一致 | 两次输入的 PIN 码不一致 | The PINs don't match |
| 开启完成 | 已开启同步 | Sync is on |
| 输入 PIN 码：正文 | 输入开启同步时设置的 8 位 PIN 码。\n\n如忘记 PIN 码，可关闭同步后重新开启。服务器上的数据将被删除，各电脑上的本机数据保留。 | Enter the 8-digit PIN set when sync was turned on.\n\nIf you forgot it, turn sync off and on again. The server copy will be deleted. Data on each computer is kept. |
| 输入 PIN 码：占位 | 8 位数字 PIN 码 | 8-digit PIN |
| PIN 码错误 | PIN 码不正确 | Incorrect PIN |
| 输入正确 | 已开始同步 | Syncing started |
| 关闭：对话框标题 | 关闭同步 | Turn off sync |
| 关闭：对话框正文 | 将立即删除服务器上的使用统计，所有电脑停止同步。本机数据保留。 | The server copy will be deleted now and all computers will stop syncing. Data on this computer is kept. |
| 关闭：按钮（`danger`） | 关闭并删除 | Turn off |
| 关闭完成 | 已关闭同步，服务器上的数据已删除 | Sync is off. The server copy was deleted |
| 关闭失败 | 复用「无法连接服务器，请检查网络后重试」，开关保持开启 | 同左 |
| 已在其他电脑开启 | 同步已在其他电脑开启，请输入在那台电脑上设置的 PIN 码。 | Sync was already turned on from another computer. Enter the PIN set there. |
| 统计页说明（同步开启时） | 数据保存至本地，并用 PIN 码加密后同步至服务器。传输时服务器将加密转发。 | Data is stored locally and synced to the server, encrypted with your PIN. During transfer, the server encrypts and forwards it. |
| 开启时点击「清除」 | 同步到服务器开启时无法清除，请先关闭同步。 | Turn off server sync before clearing usage stats. |
| 单日超限 | 某一天的使用统计超过 {limit} MB，该日未能同步。 | One day of usage stats exceeds {limit} MB and was not synced. |
| 超出上限 | 服务器上的使用统计已达 {limit} MB 上限，新记录暂未同步。请缩短保留时长后重试。 | The server copy has reached the {limit} MB limit. New records are not synced. Shorten the retention period and try again. |
| CHANGELOG | 使用统计可以用 PIN 码加密后同步到服务器，在其他电脑上登录并输入 PIN 码后即可同步；关闭同步时立即删除服务器上的数据 | Usage stats can be encrypted with a PIN and synced to the server. Sign in on another computer and enter the PIN to sync there. Turning sync off deletes the server copy immediately |

「Server sync」「Enter PIN」在 400px 下的宽度实现后按 skill 在浏览器里核对。

## 改动清单

| 项目 | 文件 | 改什么 |
|---|---|---|
| Shared | `Api/StatsSyncDtos.cs`（新） | `StatsSyncStatusDto`（开关、盐、校验值、按天列表：日期、更新时间、大小）、`EnableStatsSyncRequest`（盐、校验值）；`ApiErrorCodes` 增加 `statsSync.quotaExceeded`、`statsSync.dayTooLarge`、`statsSync.notEnabled` |
| Server | `Data/Entities`、`DeskPalDbContext`、迁移 `AddStatsSync` | `Users` 增加 `StatsSyncEnabled`、`StatsSyncSalt`、`StatsSyncKeyCheck`；新表 `StatsSyncDays`（`UserId`、`Date`、`Data`、`UpdatedAt`，主键 `UserId + Date`） |
| Server | `Features/Stats/StatsSyncEndpoints.cs`、`StatsSyncService.cs`（新） | `GET /api/me/stats-sync`、`PUT /api/me/stats-sync`（开启，已开启时返回冲突）、`DELETE /api/me/stats-sync`（关闭并删除全部）、`PUT /api/me/stats-sync/days/{date}`、`GET /api/me/stats-sync/days/{date}`。只读写当前账号，校验大小和总量；`Data` 当作不透明字节 |
| Client.Core | `Usage/SqliteUsageStore.cs` | 按天导出、按天导入（复用现有导入去重）、每天内容的哈希，找出有变化的日期 |
| Client.Core | `DeskPal.Client.Core.csproj` | 新增 NuGet 包 `Konscious.Security.Cryptography.Argon2`（纯托管实现；开发前先验证派生耗时和 nuget.org 能否还原，不合适再换 `Isopoh.Cryptography.Argon2`） |
| Client.Core | `Usage/StatsSyncCrypto.cs`（新） | PIN 码格式校验、Argon2id 派生、AES-GCM 加解密（版本字节 + nonce + tag + 密文）、校验值的生成和验证 |
| Client | `Services/StatsSyncService.cs`（新） | 登录后先下载再上传、10 分钟定时、退出时上传、开启、输入 PIN 码、关闭、发现已被其他电脑关闭时删除本机密钥 |
| Client | `Services/ApiClient.cs`、`AppSettings.cs`、`SettingsStore` | 新接口；`settings.json` 增加 `statsSync`（DPAPI 加密的密钥、每天已上传的哈希） |
| Client | 退出登录 | 删除本机密钥 |
| Client | `Ui/PanelBridge.cs` | 命令 `stats.sync.enable`（`pin`）、`stats.sync.unlock`（`pin`）、`stats.sync.disable`；`stats.clear` 同步开启时返回错误，不清除 |
| Client.Core | `Ui/PanelState.cs`、`PanelStateBuilder.cs` | `UsageView` 增加 `sync`：`enabled`、`needsPin` |
| web | `host.tsx` | 对话框输入框增加 `secret`、`digits`、`length` |
| web | `pages/StatsPage.tsx`、`bridge.ts`、`mockHost.ts`、`i18n/*` | 开关、三个对话框、说明文案切换，同步开启时点击「清除」显示提示；模拟数据三种状态 |
| Localization | `Strings.resx`、`Strings.en.resx` | 新错误码、`usage.note` 的同步版本、开启时清除的提示（如果放在 C# 侧） |
| Tests | `Usage/StatsSyncCryptoTests`、`StatsSyncServiceTests`、Server 集成测试 | 见自测矩阵 |
| 文档 | `AGENTS.md` 硬约束 4、`architecture.md`、`progress.md`、`known-issues.md`、`CHANGELOG.md` | 数据库多存一类数据：用户开启同步后上传的加密使用统计 |

## 不改什么

- 不改「公开使用统计」的请求和回复流程，房间成员查看不读服务器副本。
- 不改导入导出文件格式（`UsageExport` 版本 1）。
- 不改实时状态、`PrivacyFilter`、`presence.update`。
- 不改已有文案的值，只新增键。
- 不同步查看记录和 `settings.json` 里的其他设置（保留时长、不记录标题仍按电脑各自设置）。

## 自测矩阵

| 项 | 怎么验证 |
|---|---|
| 关闭状态下不发出任何同步请求 | `StatsSyncServiceTests`（假 `ApiClient`） |
| 开启后上传全部，之后只上传有变化的日期 | `StatsSyncServiceTests` |
| 另一台电脑输入 PIN 码后合并，重复下载不翻倍 | `StatsSyncServiceTests`（两个库互相同步） |
| A、B 交替登录，同一天两台电脑的记录都在 | `StatsSyncServiceTests` |
| PIN 码错误不保存密钥；正确时保存，之后不再要求输入 | `StatsSyncServiceTests` + `StatsSyncCryptoTests` |
| 服务器上的 `Data` 不是明文（解压、JSON 解析都失败） | Server 集成测试 |
| 关闭后该账号的 `StatsSyncDays` 为空，盐和校验值清空；Server 连不上时开关仍为开启 | Server 集成测试 + `StatsSyncServiceTests` |
| 其他电脑关闭后，本机下次同步删除密钥，界面回到未开启 | `StatsSyncServiceTests` |
| 只能读写自己账号的数据 | Server 集成测试 |
| 超过 2 MB、超过 100 MB 返回错误码，客户端显示对应文案 | Server 集成测试 + 单元测试 |
| 同步开启时 `stats.clear` 被拒绝，本机和服务器数据都不变；关闭后可以清除 | `PanelBridge` 相关测试或端到端自检 |
| 中英文三种状态、三个对话框，400px 下不折行 | 浏览器模拟数据（`?mock=stats`、`&lang=en`） |
| 真实客户端：开启、退出登录、重新登录输入 PIN 码、关闭 | 端到端自检（`deskpal-e2e-check`） |

## 风险点

1. **改变了「服务器不保存使用统计」的承诺。** 只有用户自己开启时才换统计页说明；`AGENTS.md` 硬约束 4 要加上这类数据，`progress.md` 的「使用统计」口径要改。
2. **8 位 PIN 码只能拖慢拿到数据库的人。** 一亿种组合；每次派生要 64 MB 内存，显卡并行优势很小，按本机实测（一次派生约 0.13 秒，占 4 个线程），一台 16 核电脑穷举一遍约需一个多月；高端显卡可能再快一到两个数量级，即数小时到数天（数量级估算）。它能挡住服务器管理员顺手查看、日志和备份里出现明文，大幅提高有意破解的成本，但不是不能破解。所以文案只写「用 PIN 码加密」，不写「服务器无法读取」「端到端加密」，`known-issues.md` 记一条。
3. **PIN 码丢失等于服务器上的数据丢失。** 本机数据不受影响，所以实际只丢「只在别的电脑上、还没同步过来的那部分」。
4. **服务器容量。** 带标题约 0.3 到 0.6 MB/天，压缩后约三分之一；20 个账号各存 90 天约 300 MB 到 400 MB，SQLite 可以承受，但要设上限。
5. **关闭的原子性。** 「设为关闭」和「删除数据」放在一个事务里；客户端在请求成功前不显示为关闭。