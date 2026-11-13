import { useState } from "preact/hooks";
import type { PanelState, PrivacyLevel, RoomItem } from "../bridge";
import { useHost } from "../host";
import { Avatar, Icon, Section } from "../ui";

export function RoomPage({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);

  const create = async () => {
    const a = await ask({
      title: "创建房间",
      input: { initial: `${state.me.displayName}的房间`.slice(0, 30), placeholder: "房间名称（1 到 30 个字符）", maxLength: 30, required: true },
      okText: "创建",
    });
    if (a.ok) await run({ type: "room.create", name: a.text }, "已创建");
  };
  const join = async () => {
    const a = await ask({ title: "用邀请码加入房间", input: { placeholder: "输入对方发给你的邀请码", maxLength: 20, required: true }, okText: "加入" });
    if (a.ok) await run({ type: "room.join", code: a.text }, "已加入");
  };

  return (
    <>
      <Section
        title="我的房间"
        aside={
          <div class="section-actions">
            <button onClick={create}>
              <Icon name="add" />
              创建
            </button>
            <button onClick={join}>
              <Icon name="link" />
              加入
            </button>
          </div>
        }
      >
        {state.rooms.length === 0 ? (
          <p class="infobar">
            <Icon name="info" />
            还没有房间。创建一个房间，或者用邀请码加入。
          </p>
        ) : (
          <>
            <ul class="list" role="listbox" aria-label="房间">
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
                        {r.isOwner && <span class="badge">所有者</span>}
                      </p>
                    </div>
                    <span class="row-meta">
                      {r.memberCount}/{r.maxMembers} 人
                    </span>
                  </button>
                </li>
              ))}
            </ul>
            <p class="section-note">同一时间只进入一个房间，在其他房间里显示为离线。切换不用重新输入邀请码。</p>
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
    const a = await ask({ title: "修改房间名称", input: { initial: room.name, maxLength: 30, required: true }, okText: "保存" });
    if (a.ok && a.text !== room.name) await run({ type: "room.rename", roomId: room.id, name: a.text }, "已修改");
  };
  const invite = async () => {
    const r = await run({ type: "room.invite", roomId: room.id });
    const data = r.data as { code: string; message: string } | undefined;
    if (r.ok && data) await ask({ title: "邀请码", highlight: data.code, message: data.message, okText: "知道了", cancelText: null });
  };
  const transfer = async () => {
    const a = await ask({
      title: "转让所有者",
      message: "把所有者身份转让给：",
      select: { options: others.map((m) => ({ value: m.userId, name: m.name })) },
      okText: "转让",
    });
    if (a.ok) await run({ type: "room.transfer", roomId: room.id, userId: a.selected }, "已转让");
  };
  const kick = async (userId: string, name: string) => {
    const a = await ask({
      title: "踢出房间",
      message: `确定把「${name}」踢出房间？对方会离开这个房间。邀请码换成新的之后，对方才能再次加入。`,
      okText: "踢出",
      danger: true,
    });
    if (a.ok) await run({ type: "room.kick", roomId: room.id, userId }, "已踢出");
  };
  const leave = async () => {
    const a = await ask({
      title: "退出房间",
      message: `确定退出房间「${room.name}」？退出后会从列表里移除，再次加入需要邀请码。`,
      okText: "退出",
      danger: true,
    });
    if (a.ok) await run({ type: "room.leave", roomId: room.id }, "已退出");
  };
  const dissolve = async () => {
    const a = await ask({
      title: "解散房间",
      message: `确定解散房间「${room.name}」？成员关系会一起删除，不能恢复。`,
      okText: "解散",
      danger: true,
    });
    if (a.ok) await run({ type: "room.dissolve", roomId: room.id }, "已解散");
  };

  return (
    <Section
      title={`房间「${room.name}」`}
      aside={
        room.isOwner && (
          <button class="icon-button" onClick={rename} title="修改房间名称" aria-label="修改房间名称">
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
              {state.me.displayName}（我）{room.isOwner && <span class="badge">所有者</span>}
            </p>
          </div>
        </li>
        {others.map((m) => (
          <li key={m.userId} class="list-row compact">
            <Avatar name={m.name} tone={m.view.tone} />
            <div class="row-main">
              <p class="row-title">
                {m.name}
                {m.isOwner && <span class="badge">所有者</span>}
              </p>
            </div>
            {room.isOwner && (
              <button class="subtle danger" aria-label={`把${m.name}踢出房间`} onClick={() => kick(m.userId, m.name)}>
                踢出
              </button>
            )}
          </li>
        ))}
      </ul>
      <div class="toolbar">
        <button class="primary" onClick={invite}>
          <Icon name="link" />
          生成邀请码
        </button>
        {room.isOwner && others.length > 0 && <button onClick={transfer}>转让所有者</button>}
        <span class="spacer" />
        {room.isOwner ? (
          <button class="subtle danger" onClick={dissolve}>
            解散房间
          </button>
        ) : (
          <button class="subtle danger" onClick={leave}>
            退出房间
          </button>
        )}
      </div>
    </Section>
  );
}

function Privacy({ state, room }: { state: PanelState; room: RoomItem }) {
  const { run } = useHost();
  const privacy = state.privacy!;
  const initial = Object.fromEntries(privacy.rows.map((r) => [r.category, r.level])) as Record<string, PrivacyLevel>;
  const [levels, setLevels] = useState(initial);
  const changed = privacy.rows.some((r) => levels[r.category] !== r.level);

  return (
    <Section title="隐私设置" label="隐私设置">
      <div class="card flush">
        <p class="card-note">房间「{room.name}」的成员能看到你多少信息。只影响这个房间。</p>
        {privacy.rows.map((r) => (
          <div key={r.category} class="setting-row">
            <div class="setting-text">
              <p class="setting-title">{r.name}</p>
            </div>
            <div class="setting-control">
              <select
                aria-label={`${r.name}的隐私级别`}
                value={levels[r.category]}
                onChange={(e) => setLevels({ ...levels, [r.category]: e.currentTarget.value as PrivacyLevel })}
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
      </div>
      <div class="toolbar end">
        {changed && <button onClick={() => setLevels(initial)}>撤销修改</button>}
        <button class="primary" disabled={!changed} onClick={() => run({ type: "room.privacy", roomId: room.id, levels }, "隐私设置已保存")}>
          保存隐私设置
        </button>
      </div>
    </Section>
  );
}
