# 部署 DeskPal Server

镜像由 GitHub Actions（`.github/workflows/docker.yml`）在推送默认分支时构建，推到 `ghcr.io/hulushilei/deskpal-server:latest`。HTTPS 由已有的 Traefik 终结，DeskPal 容器只提供 HTTP。

## 1. Portainer Stack

1. Portainer 新建 Stack，内容用 `stack.yml`。
2. 在 Stack 的 Environment variables 里添加 `DESKPAL_JWT_KEY`：至少 32 个字符的随机串，可用 `openssl rand -base64 48` 生成。更换后所有客户端需要重新登录。
3. 部署后在内网验证：`curl http://192.168.66.5:18820/health` 返回 `{"status":"ok"}`。

如果仓库是私有的，GHCR 镜像也是私有的，需要在 Portainer 的 Registries 里添加 `ghcr.io`（用户名 + 有 `read:packages` 权限的 token）。

## 2. Traefik

Traefik 在另一台主机上，用 file provider 转发到上面发布的端口。参考 `traefik-dynamic.example.yml`，替换域名、上游地址和 entryPoint 名称后放进 Traefik 的动态配置目录。SignalR 的 WebSocket 不需要额外配置。

## 3. 客户端

客户端安装包由 `.github/workflows/ci.yml` 产出：每次推送都会生成 Actions artifact `DeskPal-win-x64`，打 `v*` 标签时同时挂到 GitHub Release。解压后直接运行 `DeskPal.exe`，不需要安装 .NET。

登录窗口的“服务器地址”填 Traefik 对外的地址，包括端口，例如 `https://deskpal.example.com:36443`。

## 数据和配置

SQLite 数据库在 Docker 卷 `deskpal-data` 里（容器内 `/data/deskpal.db`）。只保存账号、Room、成员关系、隐私设置和分类规则，成员的最新状态只在内存里。

配置项在 `stack.yml` 的 `environment` 里按 `节__键` 的格式覆盖：

- `Rooms__MaxOwnedRoomsPerUser`：每人最多创建的 Room 数，默认 5。
- `Rooms__MaxMembersPerRoom`：新建 Room 的人数上限，默认 2。
- `Rooms__InviteLifetimeHours`：邀请码有效期，默认 24 小时。

更新内置规则库：修改 `src/DeskPal.Server/Features/Rules/builtin-rules.json`，把 `version` 加 1，推送后等镜像构建完成，在 Portainer 里重新拉取镜像并更新 Stack。Server 启动时发现版本号变化会替换规则表，客户端下次启动时自动下载新规则。