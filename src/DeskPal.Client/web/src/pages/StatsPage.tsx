import { useEffect, useRef, useState } from "preact/hooks";
import type { NoTitleKind, PanelState, StatsGranularity, StatsScale, StatsView } from "../bridge";
import { Bars, Legend, Ranking, Timeline } from "../components/charts";
import { useHost } from "../host";
import { t, type MessageKey } from "../i18n";
import { Icon, Section, SettingRow } from "../ui";

/** 正在查看的房间成员；null 表示看自己。 */
export type StatsTarget = { userId: string; name: string } | null;

const scales: { value: StatsScale; name: MessageKey }[] = [
  { value: "Day", name: "stats.day" },
  { value: "Week", name: "stats.week" },
  { value: "Month", name: "stats.month" },
];

const granularities: { value: StatsGranularity; name: MessageKey }[] = [
  { value: "State", name: "stats.byState" },
  { value: "Category", name: "stats.byCategory" },
  { value: "Title", name: "stats.byTitle" },
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
        setError(result.error ?? t("stats.loadFailed"));
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
        title={target ? t("stats.headingOf", { name: target.name }) : t("stats.heading")}
        aside={
          (sharing.length > 0 || target) && (
            <select
              aria-label={t("stats.whose")}
              value={userId ?? ""}
              onChange={(e) => {
                const id = e.currentTarget.value;
                const member = sharing.find((m) => m.userId === id);
                setView(null);
                setDate(null);
                onTarget(member ? { userId: member.userId, name: member.name } : null);
              }}
            >
              <option value="">{t("stats.myself")}</option>
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
          <div class="segmented" role="tablist" aria-label={t("stats.range")}>
            {scales.map((s) => (
              <button key={s.value} role="tab" aria-selected={scale === s.value} onClick={() => setScale(s.value)}>
                {t(s.name)}
              </button>
            ))}
          </div>
          <div class="segmented" role="tablist" aria-label={t("stats.granularity")}>
            {granularities.map((g) => (
              <button key={g.value} role="tab" aria-selected={granularity === g.value} onClick={() => setGranularity(g.value)}>
                {t(g.name)}
              </button>
            ))}
          </div>
        </div>
        <div class="stats-nav">
          <button class="icon-button" aria-label={t("stats.prev")} title={t("stats.prev")} disabled={!view?.canPrev || loading} onClick={() => go(-1)}>
            <Icon name="back" />
          </button>
          <span class="stats-title">{view?.title ?? "　"}</span>
          <button class="icon-button" aria-label={t("stats.next")} title={t("stats.next")} disabled={!view?.canNext || loading} onClick={() => go(1)}>
            <Icon name="forward" />
          </button>
          {view?.canNext && (
            <button class="subtle" onClick={() => setDate(null)}>
              {t("stats.today")}
            </button>
          )}
        </div>
        {error && (
          <p class="infobar error" role="alert">
            <Icon name="warning" />
            {error}
          </p>
        )}
        {!view && !error && <p class="loading">{target ? t("stats.requesting", { name: target.name }) : t("stats.reading")}</p>}
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
      title: t("noTitle.title"),
      message: t("noTitle.message"),
      select: {
        presentation: "radio",
        options: [
          { value: "App", name: t("noTitle.app") },
          { value: "Process", name: t("noTitle.process") },
          { value: "Domain", name: t("noTitle.domain") },
          { value: "Title", name: t("noTitle.keyword") },
        ],
      },
      input: {
        placeholders: { App: t("noTitle.exampleApp"), Process: t("noTitle.exampleProcess"), Domain: t("noTitle.exampleDomain"), Title: t("noTitle.exampleTitle") },
        maxLength: 100,
        required: true,
      },
      okText: t("common.add"),
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
      title: t("usage.clearTitle"),
      message: t("usage.clearMessage"),
      okText: t("usage.clear"),
      danger: true,
    });
    if (answer.ok) await run({ type: "stats.clear" }, t("usage.cleared"));
  };

  return (
    <Section title={t("usage.title")}>
      <div class="card flush">
        <p class="card-note">{t("usage.note")}</p>
        <SettingRow title={t("usage.viewers")} description={usage.viewers.length === 0 ? t("usage.noViewers") : undefined} />
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
          title={t("noTitle.title")}
          hint={t("noTitle.hint")}
        >
          <button onClick={() => void addNoTitle()}>
            <Icon name="add" />
            {t("common.add")}
          </button>
        </SettingRow>
        {usage.noTitleRules.length > 0 && (
          <div class="chips stats-no-title">
            {usage.noTitleRules.map((r) => (
              <button
                key={r.kind + r.pattern}
                class="chip"
                aria-label={t("noTitle.remove", { label: r.label })}
                title={t("noTitle.clickToRemove")}
                onClick={() => void run({ type: "stats.noTitle.remove", kind: r.kind, pattern: r.pattern })}
              >
                {r.label} ×
              </button>
            ))}
          </div>
        )}
        <SettingRow title={t("usage.retention")} hint={t("usage.retentionHint")}>
          <select
            aria-label={t("usage.retentionLabel")}
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
        <SettingRow title={t("usage.transfer")} hint={t("usage.transferHint")}>
          <button onClick={() => void transfer("stats.export")}>{t("usage.export")}</button>
          <button onClick={() => void transfer("stats.import")}>{t("usage.import")}</button>
        </SettingRow>
        <SettingRow title={t("usage.clearTitle")}>
          <button class="subtle danger" onClick={() => void clear()}>
            {t("usage.clear")}
          </button>
        </SettingRow>
      </div>
    </Section>
  );
}