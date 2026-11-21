// 把 /api/rules/candidates 的多人一致分类和多人推测一致的游戏写进 builtin-rules.json，并生成 PR 说明；
// 只有一两个人改过的分类和没有识别的应用列在说明里，人工判断。
// 本地试跑：node .github/scripts/merge-rule-candidates.mjs --input candidates.json
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import { parseArgs } from "node:util";

const { values: opt } = parseArgs({
  options: {
    rules: { type: "string", default: "src/DeskPal.Server/Features/Rules/builtin-rules.json" },
    body: { type: "string", default: "rule-candidates.md" },
    input: { type: "string" },
  },
});

// 内置规则库只收这些类别，Browsing 和已退役的类别不写入，见 rule builtin-rules-json。
const builtinCategories = new Set(["Work", "Communication", "Video", "Game", "Other"]);
const browsers = new Set(["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "360se", "360chrome", "qqbrowser", "sogouexplorer", "2345explorer"]);
const categoryNames = { Work: "工作", Communication: "聊天", Video: "放松 · 视频", Game: "放松 · 游戏", Browsing: "浏览网页", Other: "其他" };
const label = (c) => (c ? categoryNames[c] ?? c : "（无）");

async function loadCandidates() {
  if (opt.input) return JSON.parse(readFileSync(opt.input, "utf8"));
  const server = (process.env.DESKPAL_SERVER || "https://deskpal.hafuhafu.cn:36443").replace(/\/+$/, "");
  const res = await fetch(`${server}/api/rules/candidates`, {
    headers: { "X-DeskPal-Maintainer": process.env.DESKPAL_MAINTAINER_TOKEN ?? "" },
  });
  if (!res.ok) throw new Error(`GET /api/rules/candidates 返回 ${res.status}`);
  return res.json();
}

const data = await loadCandidates();
const file = JSON.parse(readFileSync(opt.rules, "utf8"));
const applied = [];
const skipped = [];

for (const c of data.consensus) {
  const reason =
    c.kind !== "Process" ? "只合并应用规则" :
    browsers.has(c.pattern) ? "浏览器按网站分类，不写进程规则" :
    !builtinCategories.has(c.category) ? `内置规则库不使用「${label(c.category)}」` :
    null;
  if (reason) {
    skipped.push({ ...c, reason });
    continue;
  }
  const existing = file.rules.find((r) => r.kind === c.kind && r.pattern === c.pattern);
  if (existing?.category === c.category) continue;
  if (existing) existing.category = c.category;
  else file.rules.push({ kind: c.kind, pattern: c.pattern, category: c.category, name: c.name || c.pattern });
  applied.push({ ...c, from: existing ? c.builtin ?? null : null, added: !existing });
}

// 推测出来的应用（游戏库目录、Unreal 游戏）：多人推测一致才进候选，写成内置规则后名称就固定下来，不再依赖安装路径。
const guessed = [];
for (const g of data.guessed ?? []) {
  if (g.kind !== "Process" || browsers.has(g.pattern) || !builtinCategories.has(g.guessed)) continue;
  if (file.rules.some((r) => r.kind === g.kind && r.pattern === g.pattern)) continue;
  file.rules.push({ kind: g.kind, pattern: g.pattern, category: g.guessed, name: g.displayName || g.pattern });
  guessed.push(g);
}

const oldVersion = file.version;
const changed = applied.length + guessed.length > 0;
if (changed) {
  file.version += 1;
  writeFileSync(opt.rules, JSON.stringify(file, null, 4) + "\n");
}

const cell = (s) => String(s ?? "").replaceAll("|", "\\|");
const lines = [
  `根据用户的个人分类和没有识别的应用生成。服务器上的内置规则库是版本 ${data.builtinVersion}，仓库里是版本 ${oldVersion}。`,
  "",
  "统计只计入同意分享使用数据、注册满一定天数的账号，一个账号一票。合并前请逐条确认，不对的直接在这个分支上改或删掉。",
  "",
];
if (applied.length > 0) {
  lines.push(`### 已写入 builtin-rules.json（版本 ${oldVersion} → ${file.version}）`, "", "| 进程 | 名称 | 原分类 | 新分类 | 同意人数 / 设置过的人数 |", "|---|---|---|---|---|");
  for (const c of applied) lines.push(`| \`${cell(c.pattern)}\` | ${cell(c.name)} | ${c.added ? "新增" : label(c.from)} | ${label(c.category)} | ${c.agreeing} / ${c.users} |`);
  lines.push("");
}
if (guessed.length > 0) {
  lines.push(`### 推测出来的应用，已写入 builtin-rules.json`, "", "客户端没有规则、按安装目录或 Unreal 游戏推测出类别的应用。名称取自 Steam 清单或游戏文件夹，不对的直接改。", "", "| 进程 | 名称 | 分类 | 人数 | 最近一天合计分钟 |", "|---|---|---|---|---|");
  for (const g of guessed) lines.push(`| \`${cell(g.pattern)}\` | ${cell(g.displayName)} | ${label(g.guessed)} | ${g.users} | ${g.totalMinutes} |`);
  lines.push("");
}
if (skipped.length > 0) {
  lines.push("### 一致但没有写入", "", "| 进程 | 名称 | 分类 | 人数 | 原因 |", "|---|---|---|---|---|");
  for (const c of skipped) lines.push(`| \`${cell(c.pattern)}\` | ${cell(c.name)} | ${label(c.category)} | ${c.agreeing} / ${c.users} | ${c.reason} |`);
  lines.push("");
}
const signals = data.signals ?? [];
if (signals.length > 0) {
  lines.push("### 有人改了分类，还没有达到门槛（需要人工判断）", "", "个人分类和内置规则不同、但人数或一致比例不够的应用。觉得对大多数人成立就手动改规则库。", "", "| 进程 | 名称 | 内置分类 | 改成 | 同意人数 / 设置过的人数 |", "|---|---|---|---|---|");
  for (const c of signals) lines.push(`| \`${cell(c.pattern)}\` | ${cell(c.name)} | ${c.builtin ? label(c.builtin) : "（无）"} | ${label(c.category)} | ${c.agreeing} / ${c.users} |`);
  lines.push("");
}
if (data.unknown.length > 0) {
  lines.push("### 没有识别的应用（需要人工判断）", "", "默认分类和个人分类都没有覆盖的应用，包括人数还不够、只是推测出类别的。确定类别后手动加进规则库，拿不准的不要加。", "", "| 进程 | 名称 | 推测 | 人数 | 最近一天合计分钟 |", "|---|---|---|---|---|");
  for (const u of data.unknown) lines.push(`| \`${cell(u.pattern)}\` | ${cell(u.displayName)} | ${u.guessed ? label(u.guessed) : ""} | ${u.users} | ${u.totalMinutes} |`);
  lines.push("");
}
const review = skipped.length + signals.length + data.unknown.length > 0;
if (!changed && !review) lines.push("这次没有候选。");

const body = lines.join("\n") + "\n";
writeFileSync(opt.body, body);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, body);
if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\nreview=${review}\nversion=${file.version}\n`);
console.log(`applied ${applied.length}, guessed ${guessed.length}, skipped ${skipped.length}, signals ${signals.length}, unknown ${data.unknown.length}, version ${file.version}`);
