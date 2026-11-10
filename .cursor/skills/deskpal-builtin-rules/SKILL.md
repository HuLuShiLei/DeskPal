---
name: deskpal-builtin-rules
description: 维护 DeskPal 内置分类规则库 builtin-rules.json：新增或修改应用、网站、标题关键词的分类，提高版本号，用脚本校验，确认客户端拿到新规则。用户说某个应用或网站分类不对、要补常用软件、要调整“摸鱼”判定时使用。
---

# 维护内置规则库

文件：`src/DeskPal.Server/Features/Rules/builtin-rules.json`。

## 改之前先想清楚是哪一层的问题

- 只有这一个人这样用（例如他用 B 站学习）：让他在客户端点“这个判断不对”，写入个人规则，**不要改内置规则库**。
- 只在这个 Room 里成立（例如这个 Room 的人都把飞书文档算工作）：用 Room 规则。
- 对大多数人成立：才改内置规则库。

## 步骤

1. 改 `rules` 数组，格式见 rule `builtin-rules-json`。
2. `version` 加 1。
3. 运行校验脚本：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .cursor/skills/deskpal-builtin-rules/scripts/validate-rules.ps1
   ```

   它会检查类别名、规则种类、写法（`.exe`、`www.`、协议头、大写）、标准化之后的重复项，以及浏览器进程不该有 `Process` 规则。
4. `dotnet test`。`ClassifierTests` 用的是 `TestData` 里手写的规则，不会读取真实文件；要锁定某条新规则的行为，就在里面加一条用例，规则写成和 JSON 里一样。
5. 启动 Server，日志里应该出现规则库更新到新版本。客户端下次启动时会请求 `/api/rules/builtin?version=旧版本` 拿到新规则，并缓存到 `%APPDATA%\DeskPal\builtin-rules.json`。

## 分类口径

- `Work`：IDE、Office、设计工具、开发和办公 SaaS。
- `Meeting`：会议软件。另外，麦克风被占用时，不管前台是什么都算开会。
- `Communication`：微信、QQ、企业微信、钉钉、飞书、Slack、邮箱。按“忙”处理，不算摸鱼。
- `Video` / `Music` / `Shopping` / `Social` / `News` / `Game` / `Reading`：算摸鱼。
- 拿不准的应用不要加。