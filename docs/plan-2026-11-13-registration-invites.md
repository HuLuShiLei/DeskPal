# 注册邀请码

计划已确认（2026-11-13），可以开始开发。

## 这次做什么

1. Server 可以开启「注册必须填写注册邀请码」。开启后，注册时没有填写或填写了无效的注册邀请码，注册失败。
2. 管理员账号在面板「设置」页多一块「注册邀请码」：生成注册邀请码、复制、查看哪些还没用、哪些已被谁使用、作废还没用的码。
3. 一个注册邀请码只能成功注册一个账号。

页面上叫「注册邀请码」，和房间的「邀请码」区分开。代码里叫 `SignupInvite`，避免和现有的 `RoomInvite` 混淆。

## 这次不做什么

- 不改登录。已有账号登录不需要注册邀请码，也不受开关影响。
- 不改房间邀请码（`RoomInvite`）的生成、有效期和加入房间流程。房间邀请码不能用来注册。
- 不做「每个用户都有 N 个注册邀请码」的分发方式，只有管理员能生成。
- 不做管理员的增删界面。管理员名单只在 Server 配置里改。
- 不做封禁账号、删除账号。

## 已定（2026-11-13）

| 主题 | 决定 |
|---|---|
| 谁能生成 | 管理员账号。管理员名单来自 Server 配置 `Auth:AdminUserNames`（用户名列表） |
| 在哪里生成 | 客户端设置页，只有管理员能看到这一块 |
| 使用次数 | 一码一人：成功注册一次后失效 |
| 有效期 | 不过期，只能由管理员手动作废 |
| 登录页输入框 | 注册时一直显示「注册邀请码」输入框，不加查询开关的接口。开关关闭时可以留空 |

## 行为口径

### 配置

- `Auth:RequireSignupInvite`：`true` 时注册必须填写注册邀请码。`appsettings.json` 默认 `false`，本地开发和现有测试不受影响；生产环境在 Portainer 环境变量里设为 `Auth__RequireSignupInvite=true`。
- `Auth:AdminUserNames`：管理员用户名列表，按小写比对。生产环境用环境变量 `Auth__AdminUserNames__0=<你的用户名>`。用户名不是密钥，但也不写进仓库里的 `stack.yml`。

### 注册

- `RegisterRequest` 末尾加 `SignupCode`（可空，默认 null），符合「DTO 新增字段放在最后并给默认值」。
- 校验顺序：用户名格式 → 显示名称 → 密码长度 → 用户名是否已存在 → 注册邀请码。用户名已被占用时不消耗注册邀请码。
- 开关打开时：
  - 没填：400 `auth.signupCodeRequired`
  - 码不存在、已使用、已作废：400 `auth.signupCodeInvalid`（不区分哪一种，避免被用来试探）
  - 有效：创建账号，同时在这条注册邀请码上写入 `UsedBy`、`UsedAt`，两者在同一次 `SaveChanges` 里完成。两个人同时用同一个码时，只有一个成功（用 EF 并发令牌，失败方返回 `auth.signupCodeInvalid`）。
- 开关关闭时：填了也忽略，不校验、不消耗。
- 码不区分大小写，去掉首尾空格后按大写比对，和房间邀请码一致。8 位，字母表复用房间邀请码的 `InviteAlphabet`（去掉了易混字符）。

### 管理员接口

都要求登录，且当前用户在 `Auth:AdminUserNames` 里，否则 403 `auth.notAdmin`。

| 接口 | 作用 |
|---|---|
| `GET /api/admin/signup-invites` | 列出注册邀请码：码、生成时间、过期时间、状态（未使用、已使用、已作废），已使用的带使用者的显示名称和用户名、使用时间。按生成时间倒序，最多返回 100 条 |
| `POST /api/admin/signup-invites` | 生成一个，返回它 |
| `DELETE /api/admin/signup-invites/{code}` | 作废一个未使用的码；已使用的返回 409 `auth.signupCodeUsed` |

`AuthResponse` 末尾加 `IsAdmin`（默认 false），`/api/auth/login` 和 `/api/me` 返回，客户端据此决定是否显示设置页的那一块。管理员名单改了以后，客户端下次登录或下次调用 `/api/me` 时生效；接口本身每次都按当前配置判断。

### 客户端

- 登录页「注册」标签下多一个输入框「注册邀请码」。客户端不知道 Server 有没有开开关，所以一直显示，标成「必填」还是「选填」由 Server 返回的错误码说明：开关关着时填不填都能注册。
- 设置页「账号」下面，管理员多一块「注册邀请码」：
  - 「生成」按钮：生成后弹出对话框高亮显示码，和房间邀请码的对话框一样，可以复制。
  - 列表：未使用的在前，显示码和生成时间，每行一个「作废」按钮；已使用的显示「已被 张三（zhangsan）使用」和时间。已作废的不显示。
  - 列表在打开设置页时请求一次，生成或作废后刷新。不放进 `PanelState`，由页面命令 `signupInvites.list` 的返回值显示。
- 新增文案中英文都写；新增错误码都在 `Strings*.resx` 加 `ApiError_*`。

### 存量与兼容

- 已有账号不受影响。
- 旧版本客户端没有注册邀请码输入框。Server 开启开关后，旧版本客户端注册会失败，显示通用的「操作失败」（旧版本不认识新错误码）。旧版本客户端登录正常，登录后会自动更新。新人通常直接下载最新安装包，影响很小。
- 新表通过迁移创建，不改已有表。

## 改动清单

| 文件 | 改什么 |
|---|---|
| `Shared/Api/AuthDtos.cs` | `RegisterRequest` 加 `SignupCode`；`AuthResponse` 加 `IsAdmin`；新增 `SignupInviteDto` |
| `Shared/Api/ApiErrorCodes.cs` | 新增 `auth.signupCodeRequired`、`auth.signupCodeInvalid`、`auth.signupCodeUsed`、`auth.notAdmin` |
| `Server/Data/Entities.cs`、`DeskPalDbContext` | 新增 `SignupInvite`：Id、Code（唯一索引）、CreatedBy、CreatedAt、UsedBy、UsedAt、RevokedAt，并发令牌 |
| `Server/Data/Migrations/` | 迁移 `AddSignupInvites` |
| `Server/Features/Auth/AuthOptions.cs`（新） | 两个配置项 |
| `Server/Features/Auth/SignupInviteService.cs`（新） | 生成、列出、作废、注册时校验并占用 |
| `Server/Features/Auth/AuthEndpoints.cs` | 注册接入校验；login、me 返回 `IsAdmin`；新增三个管理员接口 |
| `Server/Program.cs`、`appsettings.json` | 注册 Options 和 Service；`Auth` 节的默认值 |
| `Client/Services/ApiClient.cs` | 三个管理员接口 |
| `Client/Ui/LoginWindow.cs` | 注册时带上 `signupCode` |
| `Client/Ui/PanelBridge`（或对应文件） | 命令 `signupInvites.list`、`signupInvites.create`、`signupInvites.revoke` |
| `Client.Core/Ui/PanelState*`、`PanelStateBuilder` | `settings.isAdmin` |
| `Client.Core/Localization/Strings*.resx` | 错误码文案 |
| `web/src/pages/LoginPage.tsx`、`SettingsPage.tsx`、`bridge.ts`、`i18n/*`、`mock/` | 输入框、管理员一块、类型、文案、模拟数据 |
| `tests/DeskPal.Tests` | 见验收对照 |
| `deploy/README.md`、`docs/architecture.md`、`docs/progress.md`、`CHANGELOG.md` | 配置说明、数据模型、变更日志、更新条目 |

## 验收对照

| 验收 | 怎么验证 |
|---|---|
| 开关关闭时，不填注册邀请码也能注册 | xUnit 集成测试 |
| 开关打开时，没填、填错、已使用、已作废都注册失败，返回对应错误码 | xUnit 集成测试 |
| 开关打开时，填有效码注册成功，码变为已使用，使用者正确 | xUnit 集成测试 |
| 用户名已被占用时，注册邀请码不被消耗 | xUnit 集成测试 |
| 同一个码并发注册两次，只成功一次 | xUnit 集成测试 |
| 非管理员调用管理员接口返回 403 | xUnit 集成测试 |
| `/api/me` 和登录返回的 `IsAdmin` 正确 | xUnit 集成测试 |
| 设置页只有管理员能看到「注册邀请码」，生成、复制、作废可用 | `npm run dev` 模拟数据 + 本机 Server 和客户端端到端（`deskpal-e2e-check`） |
| 登录页注册邀请码输入框中英文、400px 宽度不溢出 | 自带浏览器看 `?page=login` 中英文 |
| 迁移能在现有数据库上执行 | 本机启动 Server，`/health` 正常 |

## 风险点

1. **生产环境开开关的顺序。** 先发布 Server 和客户端、配好管理员名单，确认自己能看到「注册邀请码」并生成，再打开 `Auth__RequireSignupInvite`。反过来的话，新人在你生成码之前完全注册不了。
2. **旧版本客户端注册失败只显示通用错误**，见「存量与兼容」。
3. **管理员名单写的是用户名。** 用户名注册后不能改，所以不会因为改名失效；但如果管理员账号的用户名拼错，设置页不会出现那一块，需要看 Server 配置。
