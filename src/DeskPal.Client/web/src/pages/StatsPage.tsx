import { useEffect, useRef, useState } from "preact/hooks";
import type { NoTitleKind, PanelState, StatsGranularity, StatsScale, StatsView } from "../bridge";
import { Bars, Legend, Ranking, Timeline } from "../components/charts";
import { useHost } from "../host";
import { Icon, Section, SettingRow } from "../ui";

/** 正在查看的房间成员；null 表示看自己。 */
export type StatsTarget = { userId: string; name: string } | null;

const scales: { value: StatsScale; name: string }[] = [
  { value: "Day", name: "日" },
  { value: "Week", name: "周" },
  { value: "Month", name: "月" },
];

const granularities: { value: StatsGranularity; name: string }[] = [
  { value: "State", name: "状态" },
  { value: "Category", name: "类别" },
  { value: "Title", name: "标题" },
];

function shift(date: string, scale: StatsScale, step: number) {
  const d = new Date(`${date}T12:00:00`);
  if (scale === "Day") d.setDate(d.getDate() + step);
  else if (scale === "Week") d.setDate(d.getDate() + step * 7);
  else d.setMonth(d.getMonth() + step, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function StatsPage({ state, target, onTarget }: { state: PanelState; target: StatsTarget; onTarget: (t: StatsTarget) => void }) {
  const { bridge } = useHost();
  const [scale, setScale] = useState<StatsScale>("Day");
  const [granularity, setGranularity] = useState<StatsGranularity>("Category");
  const [date, setDate] = useState<string | null>(null);
  const [view, setView] = useState<StatsView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const latest = useRef(0);

  const sharing = state.members.filter((m) => m.shareStats && !m.blocked);
  const userId = target?.userId ?? null;

  useEffect(() => {
    const query = async () => {
      const id = ++latest.current;
      setLoading(true);
      const result = await bridge.request({ type: "stats.query", scale, granularity, date, userId });
      if (id !== latest.current) return;
      setLoading(false);
      if (result.ok) {
        setView(result.data as StatsView);
        setError(null);
      } else {
        setError(result.error ?? "读取统计失败");
      }
    };
    void query();
    // 看自己的今天时，每分钟刷新一次。
    const timer = userId === null ? setInterval(() => void query(), 60_000) : undefined;
    return () => clearInterval(timer);
  }, [scale, granularity, date, userId]);

  const anchor = view?.anchor ?? date;
  const go = (step: number) => anchor && setDate(shift(anchor, scale, step));

  return (
    <>
      <Section
        title={target ? `${target.name} 的使用统计` : "使用统计"}
        aside={
          (sharing.length > 0 || target) && (
            <select
              aria-label="查看谁的统计"
              value={userId ?? ""}
              onChange={(e) => {
                const id = e.currentTarget.value;
                const member = sharing.find((m) => m.userId === id);
                setView(null);
                setDate(null);
                onTarget(member ? { userId: member.userId, name: member.name } : null);
              }}
            >
              <option value="">我自己</option>
              {sharing.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.name}
                </option>
              ))}
              {target && !sharing.some((m) => m.userId === target.userId) && <option value={target.userId}>{target.name}</option>}
            </select>
          )
        }
      >
        <div class="stats-controls">
          <div class="segmented" role="tablist" aria-label="时间范围">
            {scales.map((s) => (
              <button key={s.value} role="tab" aria-selected={scale === s.value} onClick={() => setScale(s.value)}>
                {s.name}
              </button>
            ))}
          </div>
          <div class="segmented" role="tablist" aria-label="细粒度">
            {granularities.map((g) => (
              <button key={g.value} role="tab" aria-selected={granularity === g.value} onClick={() => setGranularity(g.value)}>
                {g.name}
              </button>
            ))}
          </div>
        </div>
        <div class="stats-nav">
          <button class="icon-button" aria-label="上一段" title="上一段" disabled={!view?.canPrev || loading} onClick={() => go(-1)}>
            <Icon name="back" />
          </button>
          <span class="stats-title">{view?.title ?? "　"}</span>
          <button class="icon-button" aria-label="下一段" title="下一段" disabled={!view?.canNext || loading} onClick={() => go(1)}>
            <Icon name="forward" />
          </button>
          {view?.canNext && (
            <button class="subtle" onClick={() => setDate(null)}>
              回到今天
            </button>
          )}
        </div>
        {error && (
          <p class="infobar error" role="alert">
            <Icon name="warning" />
            {error}
          </p>
        )}
        {!view && !error && <p class="loading">{target ? `正在向 ${target.name} 请求统计…` : "正在读取…"}</p>}
        {view && <Report view={view} onPick={(d) => (setScale("Day"), setDate(d))} busy={loading} />}
      </Section>
      {!target && state.usage && <UsageSettings usage={state.usage} />}
    </>
  );
}

function Report({ view, onPick, busy }: { view: StatsView; onPick: (date: string) => void; busy: boolean }) {
  return (
    <div class={busy ? "stats-report busy" : "stats-report"} aria-busy={busy}>
      {view.note && (
        <p class="infobar">
          <Icon name="info" />
          {view.note}
        </p>
      )}
      <div class="card stats-summary">
        <p class="stats-total-label">{view.totalLabel}</p>
        <p class="stats-total">{view.total}</p>
        {(view.compare || view.summary) && <p class="stats-compare">{[view.compare, view.summary].filter(Boolean).join(" · ")}</p>}
      </div>
      {view.empty ? (
        <p class="stats-empty">{view.empty}</p>
      ) : (
        <>
          <div class="card stats-chart">
            {view.timeline.length > 0 && <Timeline spans={view.timeline} />}
            <Bars view={view} onPick={onPick} />
            <Legend legend={view.legend} />
          </div>
          {view.ranking.length > 0 && (
            <div class="card">
              <p class="card-label">{view.rankingTitle}</p>
              <Ranking items={view.ranking} />
            </div>
          )}
        </>
      )}
    </div>
  );
}

function UsageSettings({ usage }: { usage: NonNullable<PanelState["usage"]> }) {
  const { ask, run, toast } = useHost();

  const addNoTitle = async () => {
    const answer = await ask({
      title: "不记录标题",
      message: "符合下面条件时，统计里只记录类别和名称（网页记录域名），不记录窗口标题、网页标题和正在播放的标题。只影响之后的记录。",
      select: {
        presentation: "radio",
        options: [
          { value: "App", name: "应用名：和状态页显示的名称一致" },
          { value: "Process", name: "进程名：任务管理器「详细信息」里的名称" },
          { value: "Domain", name: "网址：按域名判断，子域名也算" },
          { value: "Title", name: "标题关键词：窗口标题或网页标题包含这个词" },
        ],
      },
      input: {
        placeholders: { App: "例如：招商银行", Process: "例如：cmb.exe", Domain: "例如：zhihu.com，也可以粘贴完整网址", Title: "例如：病历" },
        maxLength: 100,
        required: true,
      },
      okText: "添加",
    });
    if (answer.ok) await run({ type: "stats.noTitle.add", kind: answer.selected as NoTitleKind, pattern: answer.text });
  };

  const transfer = async (type: "stats.export" | "stats.import") => {
    const result = await run({ type });
    const message = (result.data as { message?: string } | undefined)?.message;
    if (result.ok && message) toast(message);
  };

  const clear = async () => {
    const answer = await ask({
      title: "清除全部统计",
      message: "删除这台电脑上这个账号的全部使用统计和查看记录，不能恢复。需要的话先导出。",
      okText: "清除",
      danger: true,
    });
    if (answer.ok) await run({ type: "stats.clear" }, "已清除");
  };

  return (
    <Section title="数据和隐私">
      <div class="card flush">
        <p class="card-note">统计只保存在这台电脑上（%APPDATA%\DeskPal\usage），不会上传，也没有加密。房间成员查看时，由你的电脑按该房间的隐私设置整理后加密发给对方，服务器只转发。</p>
        <SettingRow title="最近 7 天谁查看过" description={usage.viewers.length === 0 ? "没有人查看过" : undefined} />
        {usage.viewers.length > 0 && (
          <ul class="stats-viewers">
            {usage.viewers.map((v, i) => (
              <li key={i}>
                <span>{v.name}</span>
                <span class="sub">{v.room}</span>
                <span class="sub">{v.when}</span>
              </li>
            ))}
          </ul>
        )}
        <SettingRow
          title="不记录标题"
          hint="符合列表里任一条（应用名、进程名、网址或标题关键词）时，统计只记录类别和名称，不记录窗口标题和网页标题。适合网银、病历这类内容。房间成员看到的实时状态不受影响，仍按房间的隐私设置。"
        >
          <button onClick={() => void addNoTitle()}>
            <Icon name="add" />
            添加
          </button>
        </SettingRow>
        {usage.noTitleRules.length > 0 && (
          <div class="chips stats-no-title">
            {usage.noTitleRules.map((r) => (
              <button
                key={r.kind + r.pattern}
                class="chip"
                aria-label={`移除${r.label}`}
                title="点击移除"
                onClick={() => void run({ type: "stats.noTitle.remove", kind: r.kind, pattern: r.pattern })}
              >
                {r.label} ×
              </button>
            ))}
          </div>
        )}
        <SettingRow title="保留时长" hint="超过天数的日期只保留每天的汇总时长，没有时间轴和标题。汇总一直保留。">
          <select
            aria-label="保留详细记录的天数"
            value={usage.retentionDays}
            onChange={(e) => void run({ type: "stats.retention", days: Number(e.currentTarget.value) })}
          >
            {usage.retentionOptions.map((o) => (
              <option key={o.value} value={o.value}>
                {o.name}
              </option>
            ))}
          </select>
        </SettingRow>
        <SettingRow title="导入导出" hint="导出为 JSON 文件，可以在另一台电脑导入，重复导入不会重复计算。文件里有窗口标题，不要发给别人。">
          <button onClick={() => void transfer("stats.export")}>导出</button>
          <button onClick={() => void transfer("stats.import")}>导入</button>
        </SettingRow>
        <SettingRow title="清除全部统计">
          <button class="subtle danger" onClick={() => void clear()}>
            清除
          </button>
        </SettingRow>
      </div>
    </Section>
  );
}