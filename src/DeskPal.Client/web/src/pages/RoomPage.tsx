import { useState } from "preact/hooks";
import type { PanelState, PrivacyLevel, RoomItem } from "../bridge";
import { useHost } from "../host";
import { t } from "../i18n";
import { Avatar, Icon, Section, SettingRow, Switch } from "../ui";

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
            <ul class="list" role="listbox" aria-label={t("room.list")}>
              {state.rooms.map((r) => (
                <li key={r.id}>
                  <button
                    class="list-row selectable-row"
                    role="option"
                    aria-selected={r.id === state.selectedRoomId}
                    onClick={() => r.id !== state.selectedRoomId && run({ type: "room.select", roomId: r.id })}
                  >
                    <div class="row-main">
                      <p class="row-title">
                        {r.name}
                        {r.isOwner && <span class="badge">{t("common.owner")}</span>}
                      </p>
                    </div>
                    <span class="row-meta">
                      {t("room.memberCount", { count: r.memberCount, max: r.maxMembers })}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
            <p class="section-note">
              {room
                ? t("room.oneAtATime")
                : t("room.empty")}
            </p>
          </>
        )}
      </Section>
      {room && <RoomDetail state={state} room={room} />}
      {room && state.privacy && <Privacy key={room.id + JSON.stringify(state.privacy.rows)} state={state} room={room} />}
    </>
  );
}

function RoomDetail({ state, room }: { state: PanelState; room: RoomItem }) {
  const { ask, run } = useHost();
  const others = state.members;

  const rename = async () => {
    const a = await ask({ title: t("room.renameTitle"), input: { initial: room.name, maxLength: 30, required: true }, okText: t("common.save") });
    if (a.ok && a.text !== room.name) await run({ type: "room.rename", roomId: room.id, name: a.text }, t("common.renamed"));
  };
  const invite = async () => {
    const r = await run({ type: "room.invite", roomId: room.id });
    const data = r.data as { code: string; message: string } | undefined;
    if (r.ok && data) await ask({ title: t("room.inviteTitle"), highlight: data.code, message: data.message, okText: t("common.gotIt"), cancelText: null });
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
  const kick = async (userId: string, name: string) => {
    const a = await ask({
      title: t("room.kickTitle"),
      message: t("room.kickMessage", { name }),
      okText: t("room.kick"),
      danger: true,
    });
    if (a.ok) await run({ type: "room.kick", roomId: room.id, userId }, t("room.kicked"));
  };
  const block = async (userId: string, name: string) => {
    const a = await ask({
      title: t("room.blockTitle"),
      message: t("room.blockMessage", { name }),
      okText: t("room.block"),
      danger: true,
    });
    if (a.ok) await run({ type: "member.block", roomId: room.id, userId }, t("room.blocked"));
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
    <Section
      title={t("room.title", { name: room.name })}
      aside={
        room.isOwner && (
          <button class="icon-button" onClick={rename} title={t("room.renameTitle")} aria-label={t("room.renameTitle")}>
            <Icon name="edit" />
          </button>
        )
      }
    >
      <ul class="list">
        <li class="list-row compact">
          <Avatar name={state.me.displayName} tone={state.me.view.tone} />
          <div class="row-main">
            <p class="row-title">
              {t("common.me", { name: state.me.displayName })}{room.isOwner && <span class="badge">{t("common.owner")}</span>}
            </p>
          </div>
        </li>
        {others.map((m) => (
          <li key={m.userId} class="list-row compact">
            <Avatar name={m.name} tone={m.view.tone} />
            <div class="row-main">
              <p class="row-title">
                {m.name}
                {m.isOwner && <span class="badge">{t("common.owner")}</span>}
              </p>
            </div>
            {m.blocked ? (
              <button class="subtle" aria-label={t("room.unblockName", { name: m.name })} onClick={() => run({ type: "member.unblock", roomId: room.id, userId: m.userId }, t("room.unblocked"))}>
                {t("room.unblock")}
              </button>
            ) : (
              <button class="subtle" aria-label={t("room.blockName", { name: m.name })} onClick={() => block(m.userId, m.name)}>
                {t("room.block")}
              </button>
            )}
            {room.isOwner && (
              <button class="subtle danger" aria-label={t("room.kickName", { name: m.name })} onClick={() => kick(m.userId, m.name)}>
                {t("room.kick")}
              </button>
            )}
          </li>
        ))}
      </ul>
      <div class="toolbar">
        <button class="primary" onClick={invite}>
          <Icon name="link" />
          {t("room.invite")}
        </button>
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
    </Section>
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

  return (
    <Section title={t("privacy.title")} label={t("privacy.title")}>
      <div class="card flush">
        <p class="card-note">{t("privacy.note", { name: room.name })}</p>
        {privacy.rows.map((r) => (
          <div key={r.category} class="setting-row">
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
      </div>
    </Section>
  );
}
