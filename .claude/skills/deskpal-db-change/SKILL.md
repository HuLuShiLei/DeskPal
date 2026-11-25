---
name: deskpal-db-change
description: 修改 DeskPal Server 的 SQLite 数据库结构：改 Entity、DbContext，用 dotnet ef 生成迁移、验证、处理 SQLite 限制。用户要新增表或字段、改约束或索引，或者新功能需要持久化数据时使用。
---

# 数据库结构变更

## 先确认要不要写入数据库

| 数据 | 放在哪里 |
|---|---|
| 账号、Room、成员关系、邀请码、规则、以后的游戏进度 | 数据库 |
| 实时状态、连接、在线情况 | 内存（`PresenceStore`、`ConnectionRegistry`），**不写入数据库** |
| 活动历史（以后的时间线功能） | 留在客户端本地，Server 不保存 |

## 步骤

1. 改 `src/DeskPal.Server/Data/Entities.cs`，在 `DeskPalDbContext.OnModelCreating` 里配置约束、索引、级联删除。
   - 主键用 `Guid`，时间用 `DateTimeOffset`。
   - 字符串加 `HasMaxLength`。
   - 删除 Room 时要一起删的子表，配置 `OnDelete(DeleteBehavior.Cascade)`。
2. 生成迁移（在仓库根目录执行）：

   ```powershell
   dotnet tool restore
   dotnet ef migrations add <动词+对象，例如 AddRoomReactions> --project src/DeskPal.Server --output-dir Data/Migrations
   ```

3. 看一遍生成的迁移代码，确认没有误删列或表。
4. `dotnet build DeskPal.slnx`，然后 `dotnet test`。测试用的是临时 SQLite 文件，会执行迁移。
5. 启动一次 Server（启动时会自动执行迁移），确认 `/health` 正常。

## SQLite 的坑

- **查询里不能比较或排序 `DateTimeOffset`**，会在运行时抛异常，先查出来再在内存里处理（见 rule `server`）。
- SQLite 不支持部分 `ALTER`（例如删列、改列类型），EF 会生成重建表的迁移。数据多的时候先备份 `storage/deskpal.db`。
- **数据库目录叫 `storage/`，不要改成 `data`。** Windows 不区分大小写，`data` 就是源码目录 `Data/`，删除测试数据库时会把源码一起删掉。

## 收尾

更新 `docs/architecture.md` 的数据模型一节，在 `docs/progress.md` 记一条变更日志。