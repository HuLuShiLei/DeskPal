# 内置规则库

- **改了任何规则都要把 `version` 加 1**，否则 Server 不会替换数据库里的规则表，客户端也不会重新下载。
- `kind` 取 `Process`、`Domain`、`TitleKeyword`；`category` 必须是 `ActivityCategory` 的成员名；`name` 是展示给 Room 成员看的默认名字（中文）。`name` 含中文时必须写 `"names": { "en": "英文名" }`（`BuiltinRulesTests` 会检查），品牌名本来就是英文的不用写；`names` 的键只能是客户端支持的界面语言（目前只有 `en`）。
- `Process` 写进程名，不带 `.exe`，小写；`Domain` 写不带 `www.` 的 host，子域名会按后缀匹配；`TitleKeyword` 只对浏览器窗口标题生效。
- 浏览器进程（chrome、msedge 等）不要写 `Process` 规则。内置的 `Domain` 和 `TitleKeyword` 不参与前台浏览器的默认分类；个人分类仍按域名、标题关键词、进程名匹配。
- `folders` 是游戏库目录：`folder` 用 `\` 写（Mac 路径的 `/` 也按 `\` 匹配），以 `\` 开头；以 `\` 结尾时取下一级文件夹名作为游戏名，否则取包含它的那一级（例如 `\FINAL FANTASY XIV`）。`category` 目前都是 `Game`。改目录同样要把 `version` 加 1。不要写成 `rules` 里的新 `kind`，旧客户端读不了新的枚举值。
- 每天的 `rule-candidates.yml` 会开 PR 修改这个文件（分支 `rules/candidates`），合并前逐条确认，不对的直接在该分支上改。推测出来的游戏用的是 Steam 清单或文件夹里的英文名，需要时改成常用的中文名并补 `names.en`。
- 不要新增 `Music`、`Shopping`、`Social`、`News`、`Reading`。拿不准的应用不要加，识别不出来会显示为“正在使用 XX”，不算放松，比误判好。
- 批量修改和校验的步骤见 skill `deskpal-builtin-rules`。
