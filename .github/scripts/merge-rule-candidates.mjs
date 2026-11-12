// 把 /api/rules/candidates 的多人一致分类写进 builtin-rules.json，并生成 PR 说明。
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

const oldVersion = file.version;
if (applied.length > 0) {
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
if (skipped.length > 0) {
  lines.push("### 一致但没有写入", "", "| 进程 | 名称 | 分类 | 人数 | 原因 |", "|---|---|---|---|---|");
  for (const c of skipped) lines.push(`| \`${cell(c.pattern)}\` | ${cell(c.name)} | ${label(c.category)} | ${c.agreeing} / ${c.users} | ${c.reason} |`);
  lines.push("");
}
if (data.unknown.length > 0) {
  lines.push("### 没有识别的应用（需要人工判断）", "", "多人在用、默认分类和个人分类都没有覆盖的应用。确定类别后手动加进规则库，拿不准的不要加。", "", "| 进程 | 名称 | 人数 | 最近一天合计分钟 |", "|---|---|---|---|");
  for (const u of data.unknown) lines.push(`| \`${cell(u.pattern)}\` | ${cell(u.displayName)} | ${u.users} | ${u.totalMinutes} |`);
  lines.push("");
}
if (applied.length === 0 && skipped.length === 0 && data.unknown.length === 0) lines.push("这次没有达到门槛的候选。");

const body = lines.join("\n") + "\n";
writeFileSync(opt.body, body);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, body);
if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `changed=${applied.length > 0}\nversion=${file.version}\n`);
console.log(`applied ${applied.length}, skipped ${skipped.length}, unknown ${data.unknown.length}, version ${file.version}`);
