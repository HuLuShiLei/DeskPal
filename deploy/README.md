# 部署 DeskPal Server

镜像由 GitHub Actions（`.github/workflows/docker.yml`）在推送默认分支时构建，推到 `ghcr.io/hulushilei/deskpal-server:latest`。HTTPS 由已有的 Traefik 终结，DeskPal 容器只提供 HTTP。

## 1. Portainer Stack

1. Portainer 新建 Stack，内容用 `stack.yml`。
2. 在 Stack 的 Environment variables 里添加 `DESKPAL_JWT_KEY`：至少 32 个字符的随机串，可用 `openssl rand -base64 48` 生成。更换后所有客户端需要重新登录。
3. 部署后在内网验证：`curl http://192.168.66.5:18820/health` 返回 `{"status":"ok"}`。

如果仓库是私有的，GHCR 镜像也是私有的，需要在 Portainer 的 Registries 里添加 `ghcr.io`（用户名 + 有 `read:packages` 权限的 token）。

## 2. Traefik

Traefik 在 192.168.66.10 上，用 file provider 把 `deskpal.hafuhafu.cn`（443 和 36443 两个入口）转发到 `192.168.66.5:18820`。配置是 `traefik-dynamic.yml`，已放在 `/etc/traefik/conf/deskpal.yml`。证书用 Traefik 已有的 `*.hafuhafu.cn` 泛域名证书（DNS challenge），SignalR 的 WebSocket 不需要额外配置。

## 3. 客户端

客户端安装包由 `.github/workflows/ci.yml` 产出：每次推送都会生成 Actions artifact `DeskPal-win-x64`，打 `v*` 标签时同时挂到 GitHub Release。解压后直接运行 `DeskPal.exe`，不需要安装 .NET。

登录窗口的“服务器地址”默认是 `https://deskpal.hafuhafu.cn:36443`，一般不用改。本地开发时改成 `http://localhost:5180`。

## 数据和配置

SQLite 数据库在 Docker 卷 `deskpal-data` 里（容器内 `/data/deskpal.db`）。只保存账号、Room、成员关系、隐私设置和分类规则，成员的最新状态只在内存里。

配置项在 `stack.yml` 的 `environment` 里按 `节__键` 的格式覆盖：

- `Rooms__MaxOwnedRoomsPerUser`：每人最多创建的 Room 数，默认 5。
- `Rooms__MaxMembersPerRoom`：新建 Room 的人数上限，默认 2。
- `Rooms__InviteLifetimeHours`：邀请码有效期，默认 24 小时。

## 4. 自动更新

Portainer Community Edition 2.41.1 的容器 webhook 和 Stack webhook 都是 Business 功能，界面上的开关打不开。Stack 里的 Watchtower（`nickfedor/watchtower:1.24.0`）每 5 分钟检查一次 GHCR，发现 `latest` 有新镜像就重建 `server`。它挂了 Docker socket，但开了 `WATCHTOWER_LABEL_ENABLE`，只更新带 `com.centurylinklabs.watchtower.enable=true` 的容器。

把更新后的 `stack.yml` 贴进已有 Stack 并更新一次。之后推送 main 不用再在 Portainer 里手动拉取。更新过程中客户端会短暂断开，连上后会自动重试。

更新内置规则库：修改 `src/DeskPal.Server/Features/Rules/builtin-rules.json`，把 `version` 加 1，推送 main。镜像构建完成后，等 Watchtower 的下一轮检查。Server 启动时发现版本号变化会替换规则表，客户端下次启动时自动下载新规则。