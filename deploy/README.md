# 部署 DeskPal Server

镜像由 GitHub Actions（`.github/workflows/docker.yml`）在推送默认分支时构建，推到 `ghcr.io/hulushilei/deskpal-server:latest`。HTTPS 由已有的 Traefik 终结，DeskPal 容器只提供 HTTP。

## 1. Portainer Stack

1. Portainer 新建 Stack，内容用 `stack.yml`。
2. 在 Stack 的 Environment variables 里添加 `DESKPAL_JWT_KEY`：至少 32 个字符的随机串，可用 `openssl rand -base64 48` 生成。更换后所有客户端需要重新登录。
3. 可选：添加 `DESKPAL_MAINTAINER_TOKEN`（同样用 `openssl rand -base64 48` 生成），见第 5 节“内置规则库候选”。不添加时不影响使用，只是不会自动生成规则库候选 PR。
4. 部署后在内网验证：`curl http://192.168.66.5:18820/health` 返回 `{"status":"ok","revision":"..."}`。`revision` 是构建该镜像的 Git 提交号；本地 `dotnet run` 时为空。

如果仓库是私有的，GHCR 镜像也是私有的，需要在 Portainer 的 Registries 里添加 `ghcr.io`（用户名 + 有 `read:packages` 权限的 token）。

## 2. Traefik

Traefik 在 192.168.66.10 上，用 file provider 把 `deskpal.hafuhafu.cn`（443 和 36443 两个入口）转发到 `192.168.66.5:18820`。配置是 `traefik-dynamic.yml`，已放在 `/etc/traefik/conf/deskpal.yml`。证书用 Traefik 已有的 `*.hafuhafu.cn` 泛域名证书（DNS challenge），SignalR 的 WebSocket 不需要额外配置。

## 3. 客户端

客户端由 `.github/workflows/ci.yml` 用 Velopack 打包，再用 Inno Setup 编译成可以选择安装目录的安装向导（`installer/DeskPal.iss`），不需要安装 .NET。默认装到 `%LocalAppData%\Programs\DeskPal`，日志在安装目录的 `logs\`。

- 测试版：推送 main 且这次改了客户端时，自动发布 Pre-release，安装包是 `DeskPal-<版本>-Setup.exe`（例如 `DeskPal-0.1.2-dev.15-Setup.exe`）。安装后会继续接收测试版。只保留最近 8 个测试版。
- 正式版：推送 `vX.Y.Z` 标签（例如 `v0.2.0`）时发布，安装包是 `DeskPal-<版本>-Setup.exe`（例如 `DeskPal-0.2.0-Setup.exe`）。默认只接收正式版，在设置里勾选「加入测试版」后才会收到测试版。

已安装的客户端在启动后和每 6 小时检查一次 GitHub Release，后台下载，退出时安装。设置页可以「检查更新」或「立即重启并更新」。直接运行 `dotnet run` 编译出的程序不会检查更新。

登录窗口的“服务器地址”默认是 `https://deskpal.hafuhafu.cn:36443`，一般不用改。本地开发时改成 `http://localhost:5180`。

## 数据和配置

SQLite 数据库在 Docker 卷 `deskpal-data` 里（容器内 `/data/deskpal.db`）。只保存账号、Room、成员关系、隐私设置、内置规则、每人的个人分类，以及同意分享的用户上报的未识别应用，成员的最新状态只在内存里。

表结构变化不需要手动处理：Server 启动时自动执行还没应用的 EF Core 迁移。不要为了升级删除数据卷，否则账号和房间都会丢失。

配置项在 `stack.yml` 的 `environment` 里按 `节__键` 的格式覆盖：

- `Rooms__MaxOwnedRoomsPerUser`：每人最多创建的 Room 数，默认 5。
- `Rooms__MaxMembersPerRoom`：每个房间的人数上限，默认 20。已有房间在列出或有人加入时改成这个数。
- `Rooms__InviteLifetimeHours`：邀请码有效期，默认 24 小时。
- `Rules__MinUsers`：一个应用至少有几个账号设置过个人分类或上报过，才进入候选，默认 5。
- `Rules__MinAgreement`：个人分类里占比最高的类别至少占多少，才算多人一致，默认 0.8。
- `Rules__MinAccountAgeDays`：注册满几天的账号才计票，默认 3。
- `Rules__MaintainerToken`：`GET /api/rules/candidates` 的口令（请求头 `X-DeskPal-Maintainer`）。由 Stack 变量 `DESKPAL_MAINTAINER_TOKEN` 传入，为空时该接口返回 404。

## 4. 自动更新

Portainer 已升级到 Business Edition。Traefik 的 `traefik-portainer-webhook.yml` 只在 36443 上公开带 token 的 webhook 路径，Portainer 界面仍只在 443 上对内网开放。

GitHub Actions 在 main 的镜像推送成功后，POST 仓库 secret `PORTAINER_WEBHOOK_URL`，并带上 `tag=sha-<本次提交号>`。secret 本身是带 `:36443` 的 webhook 地址，不进仓库。Portainer 把 Stack 里的镜像改成这个不可变标签再拉取、重建。工作流接着请求 `/health`，直到返回的 `revision` 等于本次提交号，最多等 5 分钟。只返回 200 不算成功。更新过程中客户端会短暂断开，连上后会自动重试。

如果 Stack 里还有上一版的 Watchtower，用现在的 `stack.yml` 更新一次 Stack，把它去掉。`DESKPAL_JWT_KEY` 保持不动。

更新内置规则库：修改 `src/DeskPal.Server/Features/Rules/builtin-rules.json`，把 `version` 加 1，推送 main。镜像构建和 Portainer 更新都自动完成。Server 启动时发现版本号变化会替换规则表，客户端下次启动时自动下载新规则。

## 5. 内置规则库候选

用户改的分类只对自己生效（个人分类）。`.github/workflows/rule-candidates.yml` 每周一自动运行一次，也可以在 Actions 页面手动运行：

1. 请求生产环境的 `/api/rules/candidates`。只统计同意分享使用数据、注册满 `Rules__MinAccountAgeDays` 天的账号，一个账号一票。
2. 多人一致、且和当前内置规则不同的应用分类，写进 `builtin-rules.json` 并把 `version` 加 1；多人在用但没有识别的应用，只列在 PR 说明里，由人判断类别。
3. 校验规则库后创建或更新分支 `rules/candidates` 的 PR。审核、修改后合并，镜像和 Portainer 更新照常自动完成。

需要的设置：

- Stack 变量和仓库 secret 都叫 `DESKPAL_MAINTAINER_TOKEN`，值相同。没有这个 secret 时工作流直接跳过。
- 仓库 Settings → Actions → General → Workflow permissions 勾选 “Allow GitHub Actions to create and approve pull requests”。
- 可选的仓库变量 `DESKPAL_SERVER`，默认 `https://deskpal.hafuhafu.cn:36443`。

GitHub Actions 用默认 token 创建的 PR 不会自动触发 `ci.yml`，合并前在 PR 上手动运行一次 CI。
