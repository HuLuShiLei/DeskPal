import type { MemberItem, PanelState, UnclassifiedItem } from "../bridge";
import { PresenceDetail, StatusBadge, useCountdown, useHost } from "../host";
import { t } from "../i18n";
import { Avatar, Hint, HoverTip, Icon, Section, Switch } from "../ui";

export function StatusPage({ state, onViewStats }: { state: PanelState; onViewStats: (member: { userId: string; name: string }) => void }) {
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);
  return (
    <>
      <MyStatus state={state} />
      <Section title={room ? t("status.membersCount", { count: state.members.length }) : t("status.members")} label={t("status.members")}>
        {state.emptyHint && (
          <p class="infobar">
            <Icon name="info" />
            {state.emptyHint}
          </p>
        )}
        {state.members.length > 0 && (
          <ul class="list">
            {state.members.map((m) => (
              <MemberRow key={m.userId} member={m} onViewStats={() => onViewStats({ userId: m.userId, name: m.name })} />
            ))}
          </ul>
        )}
      </Section>
      {state.unclassified.length > 0 && <Unclassified state={state} />}
    </>
  );
}

function MemberRow({ member, onViewStats }: { member: MemberItem; onViewStats: () => void }) {
  const { run } = useHost();
  if (member.blocked) {
    return (
      <li class="list-row member offline" aria-label={member.name}>
        <Avatar name={member.name} tone={member.view.tone} />
        <div class="row-main">
          <p class="row-title">{member.name}</p>
          <div>
            <StatusBadge view={member.view} />
          </div>
        </div>
      </li>
    );
  }
  const label = member.pinned ? t("status.unpinName", { name: member.name }) : t("status.pinName", { name: member.name });
  return (
    <li class={member.online ? "list-row member" : "list-row member offline"} aria-label={member.name}>
      <Avatar name={member.name} tone={member.view.tone} />
      <div class="row-main">
        <p class="row-title">
          {member.name}
          {member.isOwner && <span class="badge">{t("common.owner")}</span>}
          {member.meta && <span class="row-meta">{member.meta}</span>}
        </p>
        <div>
          <StatusBadge view={member.view} />
        </div>
        <PresenceDetail view={member.view} />
      </div>
      {member.shareStats && (
        <button class="icon-button pin-button" title={t("status.viewStats")} aria-label={t("status.viewStatsOf", { name: member.name })} onClick={onViewStats}>
          <Icon name="chart" />
        </button>
      )}
      <button
        class={member.pinned ? "icon-button pin-button active" : "icon-button pin-button"}
        title={member.pinned ? t("status.unpin") : t("status.pin")}
        aria-label={label}
        aria-pressed={member.pinned}
        onClick={() => run({ type: member.pinned ? "member.unpin" : "member.pin", userId: member.userId })}
      >
        <Icon name={member.pinned ? "pinned" : "pin"} />
      </button>
    </li>
  );
}

function MyStatus({ state }: { state: PanelState }) {
  const { ask, run, toast } = useHost();
  const { me, correction } = state;

  const correct = async () => {
    if (!correction.target) {
      toast(t("status.noTarget"), true);
      return;
    }
    const a = await ask({
      title: t("status.correct"),
      message: t("status.correctMessage", { name: correction.target }),
      select: { options: correction.options, initial: correction.current },
      okText: t("common.save"),
    });
    if (a.ok) await run({ type: "presence.correct", category: a.selected }, t("status.correctSaved"));
  };

  const reset = async () => {
    const a = await ask({ title: t("status.resetTitle"), message: t("rule.removeMessage", { name: correction.target ?? "" }), okText: t("status.reset") });
    if (a.ok) await run({ type: "presence.resetCorrection" }, t("status.resetDone"));
  };

  return (
    <Section label={t("status.mine")}>
      <div class={`card me tv-${me.view.tone}`}>
        <div class="me-head">
          <Avatar name={me.displayName} tone={me.view.tone} />
          <div class="row-main">
            <div class="row-title">
              <span class="row-name">{t("common.me", { name: me.displayName })}</span>
              {me.pending ? (
                <Pending pending={me.pending} source={me.source} />
              ) : (
                me.meta && <span class="row-meta">{me.meta}</span>
              )}
            </div>
            <HoverTip text={me.full} class="me-presence">
              <p class={`me-headline tv-${me.view.tone}`}>
                <span class="status-dot" aria-hidden="true" />
                <span>{me.view.headline}</span>
                {me.source && !me.pending && <Hint text={me.source} />}
              </p>
              <PresenceDetail view={me.view} tip={false} />
            </HoverTip>
          </div>
        </div>
        <div class="toolbar">
          {!me.paused && (
            <button onClick={correct}>
              <Icon name="tag" />
              {t("status.correct")}
            </button>
          )}
          {!me.paused && correction.personal && (
            <button onClick={reset}>
              <Icon name="undo" />
              {t("status.reset")}
            </button>
          )}
          <span class="spacer" />
          <label class="inline-switch">
            {t("status.pause")}
            <Switch checked={me.paused} label={t("status.pause")} onChange={(v) => run({ type: "presence.pause", paused: v })} />
          </label>
        </div>
      </div>
    </Section>
  );
}

/** 新状态的倒计时放在标题行右侧，进度条贴在卡片底边，出现和消失都不改变卡片高度。标题行窄时「秒后变为」缩成箭头，仍是一行。 */
function Pending({ pending, source }: { pending: NonNullable<PanelState["me"]["pending"]>; source: string | null }) {
  const left = useCountdown(pending.remainingMs);
  const seconds = Math.ceil(left / 1000);
  const progress = Math.min(1, Math.max(0, 1 - left / (pending.totalSeconds * 1000)));
  const why = [source, t("pending.why", { seconds: pending.totalSeconds })].filter(Boolean).join("\n");
  return (
    <HoverTip text={pending.full ? `${pending.full}\n\n${why}` : why} class={`pending tv-${pending.view.tone}`}>
      <span class="pending-text" role="status" aria-label={seconds > 0 ? t("pending.label", { seconds, state: pending.view.headline }) : t("pending.labelSoon", { state: pending.view.headline })}>
        <Icon name="timer" class="pending-icon" />
        {seconds > 0 ? (
          <>
            <b>{seconds}</b>{t("pending.unit")}<span class="pending-verb">{t("pending.verb")}</span>
          </>
        ) : (
          <>
            {t("pending.soon")}<span class="pending-verb">{t("pending.soonVerb")}</span>
          </>
        )}
        <Icon name="forward" class="pending-arrow" />
        <span class="status-dot" aria-hidden="true" />
        <span class="pending-state">{pending.view.headline}</span>
      </span>
      <span class="progress" aria-hidden="true">
        <span style={{ width: `${progress * 100}%` }} />
      </span>
    </HoverTip>
  );
}
function Unclassified({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const classify = async (item: UnclassifiedItem) => {
    const a = await ask({
      title: t("rule.ask", { name: item.name }),
      message: t("unclassified.message"),
      select: { options: state.correction.options, initial: "Work" },
      okText: t("common.save"),
    });
    if (a.ok) await run({ type: "rules.set", kind: item.kind, pattern: item.pattern, name: item.name, category: a.selected }, t("common.saved"));
  };

  return (
    <Section title={t("unclassified.title")} aside={<span class="section-note">{t("unclassified.note")}</span>}>
      <ul class="list">
        {state.unclassified.map((item) => (
          <li key={item.pattern} class="list-row compact">
            <div class="row-main">
              <p class="row-title"><span class="row-name" title={item.name}>{item.name}</span></p>
              <p class="sub">{item.today}</p>
            </div>
            <button onClick={() => classify(item)}>{t("unclassified.classify")}</button>
          </li>
        ))}
      </ul>
    </Section>
  );
}
