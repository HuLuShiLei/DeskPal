import { useEffect, useRef, useState } from "preact/hooks";
import type { PanelState, StatsAppView, StatsDetailKey, StatsRankBy, StatsRankItem, StatsScale, StatsView } from "../bridge";
import { Bars, Heatmap, Legend, Ranking, Timeline } from "../components/charts";
import { useHost } from "../host";
import { t, type MessageKey } from "../i18n";
import { Icon, Section, SettingRow, Switch } from "../ui";

/** 正在查看的房间成员；null 表示看自己。 */
export type StatsTarget = { userId: string; name: string } | null;

const scales: { value: StatsScale; name: MessageKey }[] = [
  { value: "Day", name: "stats.day" },
  { value: "Week", name: "stats.week" },
  { value: "Month", name: "stats.month" },
];

const rankings: { value: StatsRankBy; name: MessageKey }[] = [
  { value: "Category", name: "stats.byCategory" },
  { value: "App", name: "stats.byApp" },
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
  const { bridge, ask, run } = useHost();
  const [revision, setRevision] = useState(0);
  const [scale, setScale] = useState<StatsScale>("Day");
  const [rankBy, setRankBy] = useState<StatsRankBy>("App");
  const [filter, setFilter] = useState<string | null>(null);
  const [date, setDate] = useState<string | null>(null);
  const [view, setView] = useState<StatsView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const latest = useRef(0);
  /** 正在看的详情；fromApp 表示从应用详情里的标题点进来，返回时回到应用详情。 */
  const [detail, setDetail] = useState<(StatsDetailKey & { fromApp?: boolean }) | null>(null);
  const [appView, setAppView] = useState<StatsAppView | null>(null);
  const [appError, setAppError] = useState<string | null>(null);
  const [appLoading, setAppLoading] = useState(false);
  const latestApp = useRef(0);

  const sharing = state.members.filter((m) => m.shareStats && !m.blocked);
  const userId = target?.userId ?? null;
  /** 日统计上选中的小时；换了日期、范围或查看对象就不再生效。只能选自己的统计。 */
  const [pick, setPick] = useState<{ scope: string; hours: number[] }>({ scope: "", hours: [] });
  const canPick = scale === "Day" && userId === null;
  const scope = `${date}`;
  const hours = canPick && pick.scope === scope ? pick.hours : [];
  const hoursKey = hours.join(",");
  const onHours = canPick ? (h: number[]) => setPick({ scope, hours: h }) : undefined;

  useEffect(() => {
    const query = async () => {
      const id = ++latest.current;
      setLoading(true);
      const result = await bridge.request({ type: "stats.query", scale, rankBy, filter, date, userId, hours: hours.length > 0 ? hours : null });
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
  }, [scale, rankBy, filter, date, userId, revision, hoursKey]);

  useEffect(() => {
    if (!detail) {
      setAppView(null);
      setAppError(null);
      return;
    }
    const query = async () => {
      const id = ++latestApp.current;
      setAppLoading(true);
      const result = await bridge.request({ type: "stats.app", scale, date, app: detail.app, title: detail.title });
      if (id !== latestApp.current) return;
      setAppLoading(false);
      if (result.ok) {
        setAppView(result.data as StatsAppView);
        setAppError(null);
      } else {
        setAppError(result.error ?? t("stats.loadFailed"));
      }
    };
    void query();
    const timer = setInterval(() => void query(), 60_000);
    return () => clearInterval(timer);
  }, [detail, scale, date, revision]);

  const nav = detail ? appView : view;
  const busy = detail ? appLoading : loading;
  const anchor = nav?.anchor ?? date;
  const go = (step: number) => anchor && setDate(shift(anchor, scale, step));

  const reclassify = async (app: StatsAppView) => {
    const rule = app.rule;
    if (!rule) return;
    const byTitle = rule.kind === "AppTitle" && !!detail?.title;
    const a = await ask({
      title: t("rule.ask", { name: byTitle ? detail!.title! : detail?.title ? rule.name : app.name }),
      message: t(byTitle ? "stats.reclassifyTitleMessage" : "stats.reclassifyMessage"),
      select: { options: state.correction.options, initial: rule.category },
      ...(byTitle && { input: { initial: detail!.title!, placeholder: t("status.titleKeyword"), maxLength: 150, required: true } }),
      okText: t("common.save"),
    });
    if (!a.ok || (a.selected === rule.category && (!byTitle || a.text === detail!.title))) return;
    const keyword = byTitle ? a.text : null;
    const result = await run({ type: "rules.set", kind: rule.kind, pattern: rule.pattern, name: rule.name, category: a.selected, keyword }, t("common.saved"));
    if (result.ok) setRevision((r) => r + 1);
  };

  const pickRow = (item: StatsRankItem) => {
    if (item.filter) {
      setFilter(item.filter);
      setRankBy("App");
    } else if (item.detail) {
      setDetail(item.detail);
    }
  };

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
                setFilter(null);
                setDetail(null);
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

        </div>
        <div class="stats-nav">
          <button class="icon-button" aria-label={t("stats.prev")} title={t("stats.prev")} disabled={!nav?.canPrev || busy} onClick={() => go(-1)}>
            <Icon name="back" />
          </button>
          <span class="stats-title">{nav?.title ?? "　"}</span>
          <button class="icon-button" aria-label={t("stats.next")} title={t("stats.next")} disabled={!nav?.canNext || busy} onClick={() => go(1)}>
            <Icon name="forward" />
          </button>
          {nav?.canNext && (
            <button class="subtle" onClick={() => setDate(null)}>
              {t("stats.today")}
            </button>
          )}
        </div>
        {detail ? (
          <>
            {appError && (
              <p class="infobar error" role="alert">
                <Icon name="warning" />
                {appError}
              </p>
            )}
            {!appView && !appError && <p class="loading">{t("stats.reading")}</p>}
            {appView && (
              <AppDetail
                view={appView}
                busy={appLoading}
                onBack={() => setDetail(detail.fromApp ? { app: detail.app, title: null } : null)}
                onPick={(d) => (setScale("Day"), setDate(d))}
                onReclassify={() => void reclassify(appView)}
                onTitle={(title) => setDetail({ app: detail.app, title, fromApp: true })}
              />
            )}
          </>
        ) : (
          <>
            {error && (
              <p class="infobar error" role="alert">
                <Icon name="warning" />
                {error}
              </p>
            )}
            {!view && !error && <p class="loading">{target ? t("stats.requesting", { name: target.name }) : t("stats.reading")}</p>}
            {view && (
              <Report
                view={view}
                onPick={(d) => (setScale("Day"), setDate(d))}
                onRow={pickRow}
                rankBy={rankBy}
                onRankBy={setRankBy}
                onFilter={setFilter}
                busy={loading}
                hours={hours}
                onHours={view.scale === "Day" ? onHours : undefined}
              />
            )}
          </>
        )}
      </Section>
      {!target && state.usage && <UsageSettings usage={state.usage} />}
    </>
  );
}

function Report({ view, onPick, onRow, rankBy, onRankBy, onFilter, busy, hours, onHours }: {
  view: StatsView;
  onPick: (date: string) => void;
  onRow: (item: StatsRankItem) => void;
  rankBy: StatsRankBy;
  onRankBy: (rankBy: StatsRankBy) => void;
  onFilter: (key: string | null) => void;
  busy: boolean;
  hours: number[];
  /** 日统计可以选时段时给出。 */
  onHours?: (hours: number[]) => void;
}) {
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
        {view.changes.length > 0 && (
          <ul class="stats-changes" aria-label={t("stats.changes")}>
            {view.changes.map((c) => (
              <li key={c.key} class={c.hidden ? "hidden-part" : undefined}>
                <span class="stats-swatch" style={{ background: `var(--tone-${c.tone})` }} />
                {c.name} {c.text}
              </li>
            ))}
          </ul>
        )}
        {view.bounds && <p class="stats-compare">{view.bounds}</p>}
      </div>
      {view.empty ? (
        <p class="stats-empty">{view.empty}</p>
      ) : (
        <>
          <div class="card stats-chart">
            {view.timeline.length > 0 && <Timeline spans={view.timeline} selected={hours} />}
            <Bars
              bars={view.bars}
              max={view.barMax}
              maxLabel={view.barMaxLabel}
              legend={view.legend}
              onPick={view.scale === "Day" ? undefined : onPick}
              selected={hours}
              onSelect={onHours}
            />
            <Legend legend={view.legend} selected={view.filter} onPick={onFilter} />
            {onHours && (
              <div class="stats-pick-bar">
                <span>{t(hours.length > 0 ? "stats.pickMore" : "stats.pickHint")}</span>
                {hours.length > 0 && (
                  <button class="subtle" onClick={() => onHours([])}>
                    {t("stats.wholeDay")}
                  </button>
                )}
              </div>
            )}
          </div>
          {view.rhythm && (
            <div class="card stats-chart">
              <p class="stats-card-title">{t("stats.rhythm")}</p>
              <Heatmap rhythm={view.rhythm} />
            </div>
          )}
          <div class="card stats-ranking-card">
            <div class="segmented" role="tablist" aria-label={t("stats.rankBy")}>
              {rankings.map((r) => (
                <button key={r.value} role="tab" aria-selected={rankBy === r.value} onClick={() => onRankBy(r.value)}>
                  {t(r.name)}
                </button>
              ))}
            </div>
            {view.ranking.length > 0 && <Ranking items={view.ranking} onPick={onRow} />}
            {view.ranking.length === 0 && hours.length > 0 && <p class="stats-empty">{t("stats.pickedEmpty")}</p>}
          </div>
        </>
      )}
    </div>
  );
}

function AppDetail({ view, busy, onBack, onPick, onReclassify, onTitle }: {
  view: StatsAppView;
  busy: boolean;
  onBack: () => void;
  onPick: (date: string) => void;
  onReclassify: () => void;
  onTitle: (title: string) => void;
}) {
  return (
    <div class={busy ? "stats-report busy" : "stats-report"} aria-busy={busy}>
      <div class="stats-detail-head">
        <button class="icon-button" aria-label={t("stats.back")} title={t("stats.back")} onClick={onBack}>
          <Icon name="back" />
        </button>
        <div class="stats-detail-name">
          <span class="stats-rank-name">{view.name}</span>
          {view.sub && <span class="stats-rank-sub">{view.sub}</span>}
        </div>
        {view.rule && <button onClick={onReclassify}>{t("stats.changeCategory")}</button>}
      </div>
      <div class="card stats-summary">
        <p class="stats-total-label">{view.totalLabel}</p>
        <p class="stats-total">{view.total}</p>
        {view.summary && <p class="stats-compare">{view.summary}</p>}
        {view.facts.length > 0 && (
          <dl class="stats-facts">
            {view.facts.map((f) => (
              <div key={f.label}>
                <dt>{f.label}</dt>
                <dd>{f.value}</dd>
              </div>
            ))}
          </dl>
        )}
      </div>
      {view.empty ? (
        <p class="stats-empty">{view.empty}</p>
      ) : (
        <>
          <div class="card stats-chart">
            <Bars bars={view.bars} max={view.barMax} maxLabel={view.barMaxLabel} legend={view.legend} onPick={view.scale === "Day" ? undefined : onPick} />
          </div>
          {view.hours.length > 0 && (
            <div class="card stats-chart">
              <p class="stats-card-title">{t("stats.byHour")}</p>
              <Bars bars={view.hours} max={view.hoursMax} maxLabel={view.hoursMaxLabel} legend={view.legend} label={t("stats.byHour")} />
            </div>
          )}
          {view.titles.length > 0 && (
            <div class="card stats-ranking-card">
              <p class="stats-card-title">{t("stats.topTitles")}</p>
              <Ranking items={view.titles} onPick={(item) => item.detail?.title && onTitle(item.detail.title)} />
            </div>
          )}
        </>
      )}
    </div>
  );
}

function UsageSettings({ usage }: { usage: NonNullable<PanelState["usage"]> }) {
  const { ask, run, toast } = useHost();
  const sync = usage.sync;
  const synced = sync?.enabled ?? false;
  const folder = usage.folder;
  const folderOn = folder?.enabled ?? false;
  const [syncBusy, setSyncBusy] = useState(false);

  const busy = async (action: () => Promise<unknown>) => {
    setSyncBusy(true);
    try {
      await action();
    } finally {
      setSyncBusy(false);
    }
  };

  /** 说明对话框里设置 PIN 码，再输入一次确认。取消或两次不一致时返回 null。 */
  const askNewPin = async (title: string, message: string) => {
    const first = await ask({
      title,
      message,
      input: { pin: 8, placeholder: t("sync.pinPlaceholder") },
      okText: t("sync.next"),
    });
    if (!first.ok) return null;
    const second = await ask({
      title: t("sync.confirmTitle"),
      message: t("sync.confirmMessage"),
      input: { pin: 8, placeholder: t("sync.unlockPlaceholder") },
      okText: t("sync.turnOn"),
    });
    if (!second.ok) return null;
    if (second.text !== first.text) {
      toast(t("sync.mismatch"), true);
      return null;
    }
    return first.text;
  };

  const enableSync = async () => {
    const pin = await askNewPin(t("sync.title"), t("sync.enableMessage"));
    if (pin) await busy(() => run({ type: "stats.sync.enable", pin }, t("sync.enabled")));
  };

  const unlockSync = async () => {
    const answer = await ask({
      title: t("sync.enterPin"),
      message: t("sync.unlockMessage"),
      input: { pin: 8, placeholder: t("sync.unlockPlaceholder") },
    });
    if (answer.ok) await busy(() => run({ type: "stats.sync.unlock", pin: answer.text }, t("sync.unlocked")));
  };

  const disableSync = async () => {
    const answer = await ask({
      title: t("sync.disableTitle"),
      message: t("sync.disableMessage"),
      okText: t("sync.disable"),
      danger: true,
    });
    if (answer.ok) await busy(() => run({ type: "stats.sync.disable" }, t("sync.disabled")));
  };

  const chooseFolder = async () => {
    const chosen = await run({ type: "stats.folder.choose" });
    const target = chosen.data as { path: string; existing: boolean } | undefined;
    if (!chosen.ok || !target) return;
    let pin: string | null = null;
    if (target.existing) {
      const answer = await ask({
        title: t("sync.enterPin"),
        message: t("folder.joinMessage"),
        input: { pin: 8, placeholder: t("sync.unlockPlaceholder") },
        okText: t("sync.turnOn"),
      });
      if (answer.ok) pin = answer.text;
    } else {
      pin = await askNewPin(t("folder.title"), t("folder.enableMessage"));
    }
    if (pin) await busy(() => run({ type: "stats.folder.enable", path: target.path, pin }, t("folder.enabled")));
  };

  const stopFolder = async () => {
    const answer = await ask({
      title: t("folder.stopTitle"),
      message: t("folder.stopMessage"),
      okText: t("folder.stop"),
    });
    if (answer.ok) await busy(() => run({ type: "stats.folder.stop" }, t("folder.stopped")));
  };

  const transfer = async (type: "stats.export" | "stats.import") => {
    const result = await run({ type });
    const message = (result.data as { message?: string } | undefined)?.message;
    if (result.ok && message) toast(message);
  };

  const clear = async () => {
    if (synced || folderOn) {
      toast(t(synced ? "usage.clearSyncOn" : "usage.clearFolderOn"), true);
      return;
    }
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
        <p class="card-note">{t(synced ? "usage.noteSynced" : folderOn ? "usage.noteFolder" : "usage.note")}</p>
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
        <SettingRow title={t("usage.transfer")} hint={t("usage.transferHint")}>
          <button onClick={() => void transfer("stats.export")}>{t("usage.export")}</button>
          <button onClick={() => void transfer("stats.import")}>{t("usage.import")}</button>
        </SettingRow>
        {sync && (
          <SettingRow
            title={t("sync.title")}
            hint={t("sync.hint")}
            description={
              sync.needsPin ? (
                <>
                  {t("sync.needsPin")}
                  <span class="sub setting-action">
                    <button disabled={syncBusy} onClick={() => void unlockSync()}>
                      {t("sync.enterPin")}
                    </button>
                  </span>
                </>
              ) : folderOn && !synced ? (
                t("sync.folderOn")
              ) : (
                (sync.error ?? undefined)
              )
            }
          >
            <Switch
              checked={synced}
              label={t("sync.title")}
              disabled={syncBusy || (folderOn && !synced)}
              onChange={(on) => void (on ? enableSync() : disableSync())}
            />
          </SettingRow>
        )}
        {folder && (
          <SettingRow
            title={t("folder.title")}
            hint={t("folder.hint")}
            description={
              folderOn ? (
                <>
                  {folder.unavailable && <span class="sub">{t("folder.unavailable")}</span>}
                  <span class="sub folder-path" title={folder.path ?? undefined}>
                    {folder.path}
                  </span>
                </>
              ) : synced ? (
                t("folder.serverOn")
              ) : undefined
            }
          >
            {folderOn ? (
              <button disabled={syncBusy} onClick={() => void stopFolder()}>
                {t("folder.stop")}
              </button>
            ) : (
              <button disabled={syncBusy || synced} onClick={() => void chooseFolder()}>
                {t("folder.choose")}
              </button>
            )}
          </SettingRow>
        )}
        <SettingRow title={t("usage.clearTitle")}>
          <button class="subtle danger" onClick={() => void clear()}>
            {t("usage.clear")}
          </button>
        </SettingRow>
      </div>
    </Section>
  );
}
