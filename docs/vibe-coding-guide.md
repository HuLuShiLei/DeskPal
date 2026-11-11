# 用 AI 继续开发 DeskPal

## 开新会话

Cursor 会自动读取 `AGENTS.md` 和 `.cursor/rules/` 里匹配当前文件的 rule，不需要手动贴上下文。开头一句话说清楚要做什么、属于哪个阶段即可。需要背景的时候，直接点名文档：

| 想让 AI 知道 | 让它读 |
|---|---|
| 整体怎么运作 | `docs/architecture.md` |
| 哪些已经定了、不能随便改 | `docs/progress.md` 的“已定” |
| 下一步做什么 | `docs/roadmap.md` |
| 某个现象是不是已知问题 | `docs/known-issues.md` |
| 当初为什么这么设计 | `docs/plan-v1.md` |

## 提示词模板

**做路线图里的功能**

> 做 roadmap 阶段 2 的“摸鱼同步提醒”。先读 architecture 和 progress，给我口径和改动点，确认以后再写代码。做完跑测试和端到端自检，更新 progress。

**新增消息类型 / 信号 / 数据库字段 / 规则**

> 按 skill deskpal-add-message-type 加一个 `interaction.reaction`，payload 是一个 emoji。

> 按 skill deskpal-add-signal 加摄像头占用信号，占用时和麦克风一样算开会。

> 按 skill deskpal-builtin-rules 把“语雀”“Notion”加成 Work，把“虎扑”加成 Social。

**修 bug**

> 现象：……；复现步骤：……；预期：……。先定位原因，说明以后再改，改完补一个会失败的测试。

**发布新版本**

> 发布正式版 v0.2.0：确认 build、test 通过并推送 main，再打 `v0.2.0` 标签推送，等 CI 跑完后检查 Release 里有 `DeskPal-win-Setup.exe`。测试版不用手动发：推送 main 后，已安装并加入测试版的客户端会自己检查更新。

**联调**

> 按 skill deskpal-e2e-check 自检一遍，重点看浏览器域名识别。

## 完成标准

一次改动做完，至少满足：

1. `dotnet build DeskPal.slnx` 没有错误和新警告。
2. `dotnet test` 全部通过；改了行为的，有对应的新测试或改过的断言。
3. 改了采集、Hub、界面的，跑一次 skill `deskpal-e2e-check`。
4. 改了口径（状态优先级、隐私默认值、Room 规则等）的，同步 `docs/progress.md` 的“已定”和变更日志，以及 `docs/architecture.md` 的对应章节。
5. 修掉已知问题的，从 `docs/known-issues.md` 删除。
6. 没有提交密钥、token、本机数据（`storage/`、`.local/`）。生产环境的 JWT 密钥只放在 Portainer Stack 的环境变量 `DESKPAL_JWT_KEY` 里。

## 让 AI 少走弯路

- **一次只做一件事**，做完验证再下一件。一个会话里塞太多功能，上下文会被稀释。
- **先要方案再要代码**：涉及口径的改动（谁能看到什么、什么时候算摸鱼），让 AI 先列出口径给你确认。
- **说出约束**：例如“不能读窗口内容”“旧客户端要兼容”。AGENTS.md 里的硬性约束已经写了，但具体需求里的约束还要你说。
- **发现 AI 反复犯同一个错**，就把正确做法补进对应的 rule 或 skill，下次自动生效。
- 会话太长、AI 开始忘事时，让它先把进度写进 `docs/progress.md`，再开新会话接着做。