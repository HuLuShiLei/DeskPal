---
name: deskpal-builtin-rules
description: 维护 DeskPal 内置分类规则库 builtin-rules.json：新增或修改应用、网站、标题关键词的分类，提高版本号，用脚本校验，确认客户端拿到新规则。用户说某个应用或网站分类不对、要补常用软件、要调整“摸鱼”判定时使用。
---

# 维护内置规则库

文件：`src/DeskPal.Server/Features/Rules/builtin-rules.json`。

## 改之前先想清楚是哪一层的问题

- 当前房间里的人要统一（例如这个房间把某个网站算工作）：让成员点「纠正分类」。它写入当前房间的规则，房间内所有人按这条规则识别，**不要改内置规则库**。
- 没有进入房间时，「纠正分类」只写入这台电脑的个人规则。
- 对大多数人成立：才改内置规则库。

## 步骤

1. 改 `rules` 数组，格式见 rule `builtin-rules-json`。
2. `version` 加 1。
3. 运行校验脚本：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .cursor/skills/deskpal-builtin-rules/scripts/validate-rules.ps1
   ```

   它会检查类别名、规则种类、写法（`.exe`、`www.`、协议头、大写）、标准化之后的重复项，以及浏览器进程不该有 `Process` 规则。
4. `dotnet test`。`ClassifierTests` 用的是 `TestData` 里手写的规则，不会读取真实文件；要锁定某条新规则的行为，就在里面加一条用例，规则写成和 JSON 里一样。`BuiltinRulesTests` 读取真实的 JSON，锁定办公聊天、个人聊天和 Discord 的分类，改了这几类要同步它的用例。
5. 启动 Server，日志里应该出现规则库更新到新版本。客户端下次启动时会请求 `/api/rules/builtin?version=旧版本` 拿到新规则，并缓存到 `%APPDATA%\DeskPal\builtin-rules.json`。

## 分类口径

- `Work`：IDE、Office、设计工具、开发和办公 SaaS，以及企业微信、钉钉、飞书、Slack、Teams、邮箱、会议软件。
- `Meeting`：不再产生。枚举保留，收到时显示为“工作中”。规则库版本 2 起没有这个类别的规则。
- `Music`、`Shopping`、`Social`、`News`、`Reading`：不再产生。枚举保留。规则库版本 3 起没有这些类别。购物、社交网站、新闻、阅读并进“正在浏览网页”；音乐和阅读客户端显示为“正在使用 XX”。
- `Communication`：微信、QQ、TIM、Telegram、WhatsApp、Skype 等个人聊天，显示为“在聊天”。
- `Video` / `Game`：仍算放松，显示为“在看视频”“在玩游戏”。
- 前台是浏览器时，客户端不使用内置的域名和标题规则，统一显示“正在浏览网页”，后面是域名和页面标题。想把某一个网站改成工作或视频、游戏，用个人规则或 Room 规则，不要为此改内置规则库。
- 拿不准的应用不要加。
