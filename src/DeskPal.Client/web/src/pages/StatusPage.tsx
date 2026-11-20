import { useEffect, useState } from "preact/hooks";
import type { MemberItem, PanelState, PresenceView, UnclassifiedItem } from "../bridge";
import { PresenceDetail, StatusBadge, useCountdown, useHost } from "../host";
import { t } from "../i18n";
import { Avatar, Hint, HoverTip, Icon, Section, Switch } from "../ui";

export function StatusPage({ state, onViewStats }: { state: PanelState; onViewStats: (member: { userId: string; name: string }) => void }) {
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);
  return (
    <>
      <MyStatus state={state} />
      <Section
        title={
          <>
            {t("status.members")}
            {room && state.members.length > 1 && <Hint text={t("status.dragToSort")} />}
          </>
        }
        label={t("status.members")}
      >
        {state.emptyHint && (
          <p class="infobar">
            <Icon name="info" />
            {state.emptyHint}
          </p>
        )}
        {room && state.members.length > 0 && <MemberList roomId={room.id} members={state.members} onViewStats={onViewStats} />}
      </Section>
      {state.unclassified.length > 0 && <Unclassified state={state} />}
    </>
  );
}

/** 拖动成员调整顺序，只保存在本机。宿主推回新顺序之前先按本地顺序显示，避免松手后闪回。 */
function MemberList({ roomId, members, onViewStats }: { roomId: string; members: MemberItem[]; onViewStats: (member: { userId: string; name: string }) => void }) {
  const { run } = useHost();
  const [drag, setDrag] = useState<{ id: string; order: string[] } | null>(null);
  const [saved, setSaved] = useState<string[] | null>(null);
  const hostOrder = members.map((m) => m.userId);
  const hostKey = hostOrder.join();
  useEffect(() => {
    if (saved && saved.join() === hostKey) setSaved(null);
  }, [hostKey]);

  const base = saved ? [...saved.filter((id) => hostOrder.includes(id)), ...hostOrder.filter((id) => !saved.includes(id))] : hostOrder;
  const order = drag?.order ?? base;
  const byId = new Map(members.map((m) => [m.userId, m]));
  const canDrag = members.length > 1;

  const over = (e: DragEvent, targetId: string) => {
    if (!drag) return;
    e.preventDefault();
    if (targetId === drag.id) return;
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const rest = drag.order.filter((id) => id !== drag.id);
    rest.splice(rest.indexOf(targetId) + (e.clientY > rect.top + rect.height / 2 ? 1 : 0), 0, drag.id);
    if (rest.join() !== drag.order.join()) setDrag({ ...drag, order: rest });
  };
  const end = async () => {
    if (!drag) return;
    const next = drag.order;
    setDrag(null);
    if (next.join() === base.join()) return;
    setSaved(next);
    const result = await run({ type: "member.reorder", roomId, userIds: next });
    if (!result.ok) setSaved(null);
  };

  return (
    <ul class={drag ? "list dragging" : "list"} onDragOver={(e) => drag && e.preventDefault()} onDrop={(e) => e.preventDefault()}>
      {order.map((id) => {
        const m = byId.get(id)!;
        return (
          <MemberRow
            key={id}
            member={m}
            onViewStats={() => onViewStats({ userId: m.userId, name: m.name })}
            drag={
              canDrag
                ? {
                    draggable: true,
                    onDragStart: (e: DragEvent) => {
                      e.dataTransfer?.setData("text/plain", m.name);
                      if (e.dataTransfer) e.dataTransfer.effectAllowed = "move";
                      setDrag({ id, order: base });
                    },
                    onDragOver: (e: DragEvent) => over(e, id),
                    onDragEnd: () => void end(),
                  }
                : {}
            }
            dragging={drag?.id === id}
          />
        );
      })}
    </ul>
  );
}

type DragProps = {
  draggable?: boolean;
  onDragStart?: (e: DragEvent) => void;
  onDragOver?: (e: DragEvent) => void;
  onDragEnd?: () => void;
};

/**
 * 卡片里每个元素的位置固定，不随文字长短、有无而移动：名字、状态、详情固定三行，没有详情时留空；
 * 详情只显示一行（应用名 · 标题），完整内容悬停查看；客户端标志在头像右下角。
 */
function MemberRow({ member, onViewStats, drag, dragging }: { member: MemberItem; onViewStats: () => void; drag: DragProps; dragging: boolean }) {
  const { run } = useHost();
  const dragClass = dragging ? " dragging" : "";
  if (member.blocked) {
    return (
      <li class={`list-row member offline${dragClass}`} aria-label={member.name} {...drag}>
        <Avatar name={member.name} tone={member.view.tone} />
        <div class="row-main">
          <div class="member-line">
            <p class="row-title">
              <span class="row-name">{member.name}</span>
            </p>
          </div>
          <div class="member-line">
            <StatusBadge view={member.view} />
          </div>
          <p class="member-detail" />
        </div>
      </li>
    );
  }
  const label = member.pinned ? t("status.unpinName", { name: member.name }) : t("status.pinName", { name: member.name });
  return (
    <li class={`list-row member tv-${member.view.tone}${member.online ? "" : " offline"}${dragClass}`} aria-label={member.name} {...drag}>
      <Avatar name={member.name} tone={member.view.tone} client={member.client} />
      <div class="row-main">
        <div class="member-line">
          <p class="row-title">
            <span class="row-name">{member.name}</span>
            {member.isOwner && <span class="badge">{t("common.owner")}</span>}
          </p>
          <div class="member-actions">
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
          </div>
        </div>
        <div class="member-line">
          <StatusBadge view={member.view} />
          {member.meta && <span class="member-meta">{member.meta}</span>}
        </div>
        <MemberDetail view={member.view} />
      </div>
    </li>
  );
}

function MemberDetail({ view }: { view: PresenceView }) {
  if (!view.app && !view.detail) return <p class="member-detail" />;
  const showDetail = view.detail && view.detail.trim() !== view.app?.trim();
  return (
    <p class="member-detail" title={[view.app, view.detail].filter(Boolean).join("\n")}>
      {view.app && <span class="member-detail-app">{view.app}</span>}
      {view.app && showDetail && <span class="member-detail-sep"> · </span>}
      {showDetail && view.detail}
    </p>
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
          <Avatar name={me.displayName} tone={me.view.tone} client={me.client} />
          <div class="row-main">
            <div class="row-title">
              <span class="row-name">{me.displayName}</span>
              {me.pending ? (
                <Pending pending={me.pending} source={me.source} />
              ) : (
                me.meta && <span class="row-meta">{me.meta}</span>
              )}
            </div>
            <HoverTip text={[me.full, !me.pending && me.source].filter(Boolean).join("\n\n") || null} class="me-presence">
              <p class={`me-headline tv-${me.view.tone}`}>
                <span class="status-dot" aria-hidden="true" />
                <span>{me.view.headline}</span>
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
    <Section
      title={
        <>
          {t("unclassified.title")}
          <Hint text={t("unclassified.note")} />
        </>
      }
      label={t("unclassified.title")}
    >
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
