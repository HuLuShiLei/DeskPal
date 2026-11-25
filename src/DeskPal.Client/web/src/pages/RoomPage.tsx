import { useEffect, useState } from "preact/hooks";
import type { MemberItem, PanelState, PrivacyLevel, RoomItem } from "../bridge";
import { useHost } from "../host";
import { t } from "../i18n";
import { Avatar, categoryTone, Icon, memberTone, MoreMenu, RoomBadge, roomTone, Section, SettingRow, Switch, type MenuEntry } from "../ui";

export function RoomPage({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);

  const create = async () => {
    const a = await ask({
      title: t("room.createTitle"),
      input: { initial: t("room.defaultName", { name: state.me.displayName }).slice(0, 30), placeholder: t("room.namePlaceholder"), maxLength: 30, required: true },
      okText: t("room.create"),
    });
    if (a.ok) await run({ type: "room.create", name: a.text }, t("room.created"));
  };
  const join = async () => {
    const a = await ask({ title: t("room.joinTitle"), input: { placeholder: t("room.joinPlaceholder"), maxLength: 20, required: true }, okText: t("room.join") });
    if (a.ok) await run({ type: "room.join", code: a.text }, t("room.joined"));
  };

  return (
    <>
      <Section
        title={t("room.mine")}
        aside={
          <div class="section-actions">
            <button onClick={create}>
              <Icon name="add" />
              {t("room.create")}
            </button>
            <button onClick={join}>
              <Icon name="link" />
              {t("room.join")}
            </button>
          </div>
        }
      >
        {state.rooms.length === 0 ? (
          <p class="infobar">
            <Icon name="info" />
            {t("room.empty")}
          </p>
        ) : (
          <>
            <ul class="card flush list-card" role="listbox" aria-label={t("room.list")}>
              {state.rooms.map((r) => {
                const selected = r.id === state.selectedRoomId;
                return (
                  <li key={r.id}>
                    <button class="card-row selectable-row" role="option" aria-selected={selected} onClick={() => !selected && run({ type: "room.select", roomId: r.id })}>
                      <RoomBadge id={r.id} name={r.name} />
                      <div class="row-main">
                        <p class="row-title">
                          <span class="row-name">{r.name}</span>
                          {r.isOwner && <span class="badge">{t("common.owner")}</span>}
                        </p>
                        <RoomCounts room={r} />
                      </div>
                      {selected && <Icon name="check" class="row-check" />}
                    </button>
                  </li>
                );
              })}
            </ul>
            <p class="section-note">{room ? t("room.oneAtATime") : t("room.empty")}</p>
          </>
        )}
      </Section>
      {room && <RoomDetail state={state} room={room} />}
      {room && state.privacy && <Privacy key={room.id + JSON.stringify(state.privacy.rows)} state={state} room={room} />}
    </>
  );
}

function RoomCounts({ room }: { room: RoomItem }) {
  return (
    <p class="sub room-counts">
      <span>
        <Icon name="people" />
        {t("room.memberCount", { count: room.memberCount, max: room.maxMembers })}
      </span>
      {room.onlineCount != null && (
        <span>
          <span class="online-dot" />
          {t("app.onlineCount", { count: room.onlineCount })}
        </span>
      )}
    </p>
  );
}

function RoomDetail({ state, room }: { state: PanelState; room: RoomItem }) {
  const { ask, run } = useHost();
  const others = state.members;

  const rename = async () => {
    const a = await ask({ title: t("room.renameTitle"), input: { initial: room.name, maxLength: 30, required: true }, okText: t("common.save") });
    if (a.ok && a.text !== room.name) await run({ type: "room.rename", roomId: room.id, name: a.text }, t("common.renamed"));
  };
  const transfer = async () => {
    const a = await ask({
      title: t("room.transferTitle"),
      message: t("room.transferMessage"),
      select: { options: others.map((m) => ({ value: m.userId, name: m.name })) },
      okText: t("room.transferOk"),
    });
    if (a.ok) await run({ type: "room.transfer", roomId: room.id, userId: a.selected }, t("room.transferred"));
  };
  const leave = async () => {
    const a = await ask({
      title: t("room.leaveTitle"),
      message: t("room.leaveMessage", { name: room.name }),
      okText: t("room.leave"),
      danger: true,
    });
    if (a.ok) await run({ type: "room.leave", roomId: room.id }, t("room.left"));
  };
  const dissolve = async () => {
    const a = await ask({
      title: t("room.dissolveTitle"),
      message: t("room.dissolveMessage", { name: room.name }),
      okText: t("room.dissolve"),
      danger: true,
    });
    if (a.ok) await run({ type: "room.dissolve", roomId: room.id }, t("room.dissolved"));
  };

  return (
    <>
      <Section title={t("room.current")}>
        <div class={`card room-hero tv-${roomTone(room.id)}`}>
          <div class="room-hero-head">
            <RoomBadge id={room.id} name={room.name} size="large" />
            <div class="row-main">
              <p class="row-title room-hero-name">
                <span class="row-name">{room.name}</span>
                {room.isOwner && <span class="badge">{t("common.owner")}</span>}
                {room.isOwner && (
                  <button class="icon-button" onClick={rename} title={t("room.renameTitle")} aria-label={t("room.renameTitle")}>
                    <Icon name="edit" />
                  </button>
                )}
              </p>
              <RoomCounts room={room} />
            </div>
          </div>
          <InviteCode room={room} />
          <div class="toolbar">
            {room.isOwner && others.length > 0 && <button onClick={transfer}>{t("room.transferTitle")}</button>}
            <span class="spacer" />
            {room.isOwner ? (
              <button class="subtle danger" onClick={dissolve}>
                {t("room.dissolveTitle")}
              </button>
            ) : (
              <button class="subtle danger" onClick={leave}>
                {t("room.leaveTitle")}
              </button>
            )}
          </div>
        </div>
      </Section>
      <Section title={t("status.members")}>
        <ul class="card flush list-card">
          <li class="card-row room-member">
            <Avatar name={state.me.displayName} tone={memberTone(state.me.userId)} />
            <div class="row-main">
              <p class="row-title">
                <span class="row-name">{t("common.me", { name: state.me.displayName })}</span>
                {room.isOwner && <span class="badge">{t("common.owner")}</span>}
              </p>
              {room.joined && <p class="sub">{room.joined}</p>}
            </div>
          </li>
          {others.map((m) => (
            <MemberRow key={m.userId} room={room} member={m} />
          ))}
        </ul>
      </Section>
    </>
  );
}

/** 邀请码不在 PanelState 里：打开房间页或切换房间时向宿主要一次。复制时宿主会再向服务器取最新的，其他人刚重新生成过也不会复制到旧码。 */
function InviteCode({ room }: { room: RoomItem }) {
  const { ask, run, bridge } = useHost();
  const [code, setCode] = useState<string | null>(null);

  useEffect(() => {
    let current = true;
    setCode(null);
    void bridge.request({ type: "room.inviteGet", roomId: room.id }).then((r) => {
      if (current && r.ok) setCode((r.data as { code: string }).code);
    });
    return () => {
      current = false;
    };
  }, [room.id]);

  const copy = async () => {
    const r = await run({ type: "room.inviteCopy", roomId: room.id }, t("room.inviteCopied"));
    if (r.ok) setCode((r.data as { code: string }).code);
  };
  const renew = async () => {
    const a = await ask({ title: t("room.renewTitle"), message: t("room.renewMessage"), okText: t("room.renew"), danger: true });
    if (!a.ok) return;
    const r = await run({ type: "room.invite", roomId: room.id }, t("room.renewed"));
    if (r.ok) setCode((r.data as { code: string }).code);
  };

  if (code === null) return null;
  return (
    <div class="invite-box">
      <div class="invite-row">
        <div class="invite-main">
          <p class="invite-label">{t("room.inviteTitle")}</p>
          <p class="invite-value selectable">{code}</p>
        </div>
        <button onClick={copy} aria-label={t("room.inviteCopyName")}>
          <Icon name="copy" />
          {t("room.inviteCopy")}
        </button>
        {room.isOwner && (
          <button class="icon-button" onClick={renew} title={t("room.renewTitle")} aria-label={t("room.renewTitle")}>
            <Icon name="refresh" />
          </button>
        )}
      </div>
      <p class="invite-hint">{t("room.inviteHint")}</p>
    </div>
  );
}

function MemberRow({ room, member: m }: { room: RoomItem; member: MemberItem }) {
  const { ask, run } = useHost();

  const kick = async () => {
    const a = await ask({ title: t("room.kickTitle"), message: t("room.kickMessage", { name: m.name }), okText: t("room.kick"), danger: true });
    if (a.ok) await run({ type: "room.kick", roomId: room.id, userId: m.userId }, t("room.kicked"));
  };
  const block = async () => {
    const a = await ask({ title: t("room.blockTitle"), message: t("room.blockMessage", { name: m.name }), okText: t("room.block"), danger: true });
    if (a.ok) await run({ type: "member.block", roomId: room.id, userId: m.userId }, t("room.blocked"));
  };

  const items: MenuEntry[] = [
    m.blocked
      ? { label: t("room.unblock"), icon: "block", onSelect: () => void run({ type: "member.unblock", roomId: room.id, userId: m.userId }, t("room.unblocked")) }
      : { label: t("room.block"), icon: "block", onSelect: () => void block() },
  ];
  if (room.isOwner) items.push({ label: t("room.kickTitle"), icon: "remove", danger: true, onSelect: () => void kick() });

  return (
    <li class={m.blocked ? "card-row room-member blocked" : "card-row room-member"}>
      <Avatar name={m.name} tone={memberTone(m.userId)} />
      <div class="row-main">
        <p class="row-title">
          <span class="row-name">{m.name}</span>
          {m.isOwner && <span class="badge">{t("common.owner")}</span>}
          {m.blocked && <span class="badge muted">{t("room.blocked")}</span>}
        </p>
        {m.joined && <p class="sub">{m.joined}</p>}
      </div>
      <MoreMenu label={t("room.manageName", { name: m.name })} items={items} />
    </li>
  );
}

function Privacy({ state, room }: { state: PanelState; room: RoomItem }) {
  const { ask, run } = useHost();
  const privacy = state.privacy!;
  const initial = Object.fromEntries(privacy.rows.map((r) => [r.category, r.level])) as Record<string, PrivacyLevel>;
  const [levels, setLevels] = useState(initial);

  const change = async (category: string, level: PrivacyLevel) => {
    const next = { ...levels, [category]: level };
    setLevels(next);
    const result = await run({ type: "room.privacy", roomId: room.id, levels: next }, t("privacy.saved"));
    if (!result.ok) setLevels(initial);
  };

  const share = async (enabled: boolean, days: number) => {
    if (enabled && !privacy.shareStats) {
      const detailed = privacy.rows.filter((r) => levels[r.category] === "Detail").map((r) => r.name);
      const answer = await ask({
        title: t("privacy.share"),
        message:
          t("privacy.shareMessage", { room: room.name, days: privacy.dayOptions.find((o) => o.value === days)?.name ?? days }) +
          (detailed.length > 0 ? t("privacy.shareDetail", { names: detailed.join(t("common.listSeparator")) }) : ""),
        okText: t("privacy.shareOk"),
      });
      if (!answer.ok) return;
    }
    await run({ type: "room.shareStats", roomId: room.id, enabled, days }, enabled ? t("privacy.shared") : t("privacy.unshared"));
  };

  const sharePaused = async (enabled: boolean) => {
    if (enabled) {
      const answer = await ask({
        title: t("privacy.paused"),
        message: t("privacy.pausedMessage", { room: room.name }),
        okText: t("privacy.shareOk"),
      });
      if (!answer.ok) return;
    }
    await run({ type: "room.sharePausedStats", roomId: room.id, enabled }, t("privacy.saved"));
  };

  return (
    <Section title={t("privacy.title")} label={t("privacy.title")}>
      <div class="card flush">
        <p class="card-note">{t("privacy.note", { name: room.name }) + t("privacy.redacted")}</p>
        {privacy.rows.map((r) => (
          <div key={r.category} class="setting-row">
            <span class={`status-dot tv-${categoryTone(r.category)}`} aria-hidden="true" />
            <div class="setting-text">
              <p class="setting-title">{r.name}</p>
            </div>
            <div class="setting-control">
              <select
                aria-label={t("privacy.levelOf", { name: r.name })}
                value={levels[r.category]}
                onChange={(e) => void change(r.category, e.currentTarget.value as PrivacyLevel)}
              >
                {privacy.levels.map((l) => (
                  <option key={l.value} value={l.value}>
                    {l.name}
                  </option>
                ))}
              </select>
            </div>
          </div>
        ))}
        <SettingRow
          title={t("privacy.share")}
          hint={t("privacy.shareHint")}
          description={privacy.shareStats ? t("privacy.shareDays", { days: privacy.dayOptions.find((o) => o.value === privacy.statsDays)?.name ?? privacy.statsDays }) : undefined}
        >
          <Switch checked={privacy.shareStats} label={t("privacy.share")} onChange={(v) => void share(v, privacy.statsDays)} />
        </SettingRow>
        {privacy.shareStats && (
          <SettingRow title={t("privacy.days")}>
            <select aria-label={t("privacy.daysLabel")} value={privacy.statsDays} onChange={(e) => void share(true, Number(e.currentTarget.value))}>
              {privacy.dayOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.name}
                </option>
              ))}
            </select>
          </SettingRow>
        )}
        {privacy.shareStats && (
          <SettingRow title={t("privacy.paused")} hint={t("privacy.pausedHint")}>
            <Switch checked={privacy.sharePausedStats} label={t("privacy.paused")} onChange={(v) => void sharePaused(v)} />
          </SettingRow>
        )}
      </div>
    </Section>
  );
}