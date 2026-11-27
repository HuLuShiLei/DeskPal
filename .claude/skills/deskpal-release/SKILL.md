---
name: deskpal-release
description: DeskPal 推送 main、写 CHANGELOG、打正式版标签和发版的步骤与坑。用户说「推送 main」「合并到 main」「打 tag」「发正式版」「写更新日志」时使用；平时在 agent 分支上开发、提交、推送不用。
---

# DeskPal 发布

推送 main 会直接影响用户：Server 镜像自动构建并部署到生产，改了客户端时 CI 还会发测试版，已加入测试版的客户端会自己更新。所以 **main 只在用户明确要求时推送**，平时在 `agent/` 分支上提交。

## 推送 main 之前：写 CHANGELOG

1. 把这次要推送、用户能感受到的改动整理成书面中文条目，写进根目录 `CHANGELOG.md` 最上面一节，和代码一起推送。文案写法按 skill `deskpal-ui-copy`。
2. 最上面一节是下一个正式版的版本号（例如 `## 0.1.2`），测试版的改动也写在这一节。发布正式版 `vX.Y.Z` 后，下一次推送先新开 `## X.Y.(Z+1)`。
3. 每条下面缩进一行 `en:` 写英文。客户端更新后第一次打开面板时，按界面语言显示还没看过的条目；看过与否按中文比对，改了中文会再显示一次。
4. CI 发布时检查最上面一节的版本号：正式版必须和标签一致，测试版不能低于自动算出的版本号，否则发布失败。改了客户端却没改 `CHANGELOG.md` 时只给警告。

## 打正式版标签

用户说「打 tag」或「发正式版」时照下面做：

1. 标签打在当前 `main` 的提交上。这一提交的 `CHANGELOG.md` 最上面一节必须已经是 `## X.Y.Z`，和 `vX.Y.Z` 相等；先把最上面改成下一版再打标签，正式版会失败。用注解标签：`git tag -a vX.Y.Z -m "DeskPal vX.Y.Z"`，只推送这个标签。不要打 `vX.Y.Z-dev.N`，那是 CI 创建的测试版标签。
2. 推送标签前跑 `dotnet test tests/DeskPal.Tests --filter ChangelogTests`。`Shipped_changelog_has_english_for_every_item` 写死了有条目的版本列表；`Changelog.Parse` 丢掉没有条目的节，所以列表要和有内容的节一致，每条都要有 `en:`。（v0.1.3 第一次打标签时日志已经是 0.1.3，测试仍停在 0.1.2，测试任务失败，没有发出 Release。）
3. 等这次标签的 CI（测试、版本检查、Windows、macOS、Linux）全部成功，再往 `main` 推下一节。失败时保持最上面仍是 `## X.Y.Z`，改完后删掉远端标签再重打。不要提前推送 `## X.Y.(Z+1)`：测试版按远端最新正式版标签的补丁号加 1 计算，标签还没上去时对不上；修标签的提交也会落在新节之后，重打时最上面一节和标签不相等，正式版再次失败。
4. 标签 CI 通过后，立刻在最上面新开空的 `## X.Y.(Z+1)` 并推送 `main`（想跳主版本或次版本就写成那个号，例如 `## 0.2.0`）。空节不会被解析，`ChangelogTests` 不用改。这次推送会发 `X.Y.(Z+1)-dev.1`，等这次 CI 也通过。
5. 之后往新节写入第一条时，同一次提交里把 `ChangelogTests` 的版本列表改成包含这一节。

## 各平台产物

同一个版本号、同一个 Release：

| 平台 | 安装包 | 更新通道（测试版） |
|---|---|---|
| Windows | `DeskPal-<版本>-Setup.exe`（Inno Setup 向导内嵌 Velopack） | `win`（`beta`） |
| macOS | `DeskPal-<版本>-osx-arm64-Setup.pkg`、`osx-x64` | `osx-arm64`、`osx-x64`（加 `-beta`） |
| Linux | `DeskPal-<版本>-linux-x64.AppImage` | `linux-x64`（`linux-x64-beta`） |

- macOS 默认不签名；仓库变量 `MAC_SIGN=true` 并配好 `ci.yml` 里列出的 secret 后签名并公证。
- 在其他分支手动运行 CI 只打包不发布，安装包在 Artifacts 里。
- Server 镜像和部署流程见 `docs/architecture.md` 的「部署和发布」与 `deploy/README.md`。Portainer webhook 地址在 GitHub secret `PORTAINER_WEBHOOK_URL`，不进仓库。
