# 暂停分享期间的使用统计是否公开

日期：2026-11-18。状态：已确认并实现。用户选择：不允许时显示为「暂停分享」；已公开的房间默认关闭。

## 做什么

1. 开启「暂停分享」后，本机记录的使用统计片段带上「暂停期间」标记。记录内容不变，仍按没有暂停时的判定记录。
2. 房间设置的「公开使用统计」下面，在「可查看最近」之后新增一个开关「包含暂停分享期间」。和「可查看最近」一样，只有打开「公开使用统计」时才显示。按房间设置，默认关闭。
3. 关闭时，该房间成员查看我的统计时，暂停期间的片段不按实际活动显示（具体显示方式见「需要你定的」第 1 条）；打开时和现在一样，按该房间的隐私级别显示。
4. 自己在统计页看到的内容不变，暂停期间照常按实际活动统计。

## 口径

- 标记按片段记录：切换暂停时开始新的片段，同一片段不会一半暂停、一半没暂停。
- 过滤在本人客户端完成，在 `StatsFilter.Apply` 之前替换，发出去的密文里没有暂停期间的应用和标题。Server 只保存开关，不参与过滤。
- 和上一周期的对比（`Previous`）同样按开关处理。
- 已有记录没有标记：本功能上线前暂停期间记录的数据无法区分，按未暂停处理。
- 已折成按天汇总（`DailyRollups`）的日期也保留标记，汇总的主键加上这一列。
- 同一账号的其他电脑通过同步、同步文件夹、导入拿到的记录带着标记。其他电脑还是旧版本时，它导出时会丢掉标记，只影响那台电脑回复的统计。
- 这改变了进度文件里「暂停分享不影响别人查看」的已定口径，确认后改成「暂停期间的统计默认不给房间成员看，可按房间打开」。

## 改哪里

- `DeskPal.Shared/Presence/PrivacyProfile.cs`：增加 `SharePausedStats`，默认 false。
- `DeskPal.Shared/Api/RoomDtos.cs`：`PrivacyProfileDto` 末尾增加 `bool? SharePausedStats = null`，null 保留原值。
- `DeskPal.Server/Features/Rooms/RoomService.cs`：保存和读取这个字段（存在成员的 `PrivacyProfileJson` 里，不需要数据库迁移）。
- `DeskPal.Client.Core/Usage/UsageSegment.cs`、`UsageRecorder.cs`：片段和按天汇总增加 `Paused`，`Record` 多一个参数，是否暂停参与「是否开始新片段」的判断。
- `DeskPal.Client.Core/Usage/SqliteUsageStore.cs`：`Segments` 加 `Paused` 列，`DailyRollups` 主键加 `Paused`（复制表升级），读写、折叠、导出、导入、合并都带上。
- `DeskPal.Client.Core/Usage/UsageExport.cs`：`ExportedSegment`、`ExportedRollup` 末尾加 `Paused = false`，旧文件读出来是 false，`CurrentVersion` 不变。
- `DeskPal.Client.Core/Usage/`：新增把暂停片段替换掉的步骤，`UsageService.Report` 增加参数，给房间成员生成报告时按开关调用。
- `DeskPal.Client/Services/PresenceCoordinator.cs`：把 `_settings.Paused` 传给记录器。
- `DeskPal.Client/Services/StatsExchange.cs`：按房间的开关生成报告，缓存键带上开关。
- `DeskPal.Client/Services/RoomState.cs`、`Ui/PanelBridge.cs`：读取和保存开关，新命令 `room.sharePausedStats`。
- `DeskPal.Client.Core/Ui/PanelState.cs`、`PanelStateBuilder.cs`、`web/src/bridge.ts`、`web/src/pages/RoomPage.tsx`、`web/src/mock/`、`web/src/i18n/`：页面开关、模拟数据、中英文文案。
- 测试：`UsageRecorder` 切换暂停开始新片段；`SqliteUsageStore` 升级旧库、折叠、导出导入保留标记；报告过滤；`RoomService` 保存开关；`PanelStateBuilder` 和 fixtures。
- 文档：`docs/progress.md`「已定」和变更日志、`docs/architecture.md` 的统计和隐私段落、`CHANGELOG.md`（推送前）。

## 不改什么

- 实时状态：暂停时房间成员看到的仍是「暂停分享」。
- 自己的统计页、作息、详情、同步到服务器的密文格式版本号。
- 「公开使用统计」「可查看最近」现有的行为和文案。
- `RoomMemberDto`：请求方不需要知道这个开关。

## 需要你定的

1. 不允许时，房间成员看到的暂停期间怎么显示。推荐：显示为「暂停分享」，时间轴上是一段暂停，不算使用时间，没有类别、应用和标题，和实时状态里成员已经看到的一致。另一种：当作没有记录，时间轴上空白。
2. 已经打开「公开使用统计」的房间，新开关默认关还是开。推荐默认关：升级后成员不再看到暂停期间的实际活动，比现在更保守。

## 验证档位

大改：改了本机统计的持久化格式、同步格式和 Server 隐私设置。build、全量单元测试、`npm run dev` 截图检查房间设置（中英文、深浅色、400px），再按 `deskpal-e2e-check` 做一次端到端：模拟成员请求统计，分别验证开关关闭和打开时暂停期间的显示。端到端前需要你先退出本机 DeskPal。