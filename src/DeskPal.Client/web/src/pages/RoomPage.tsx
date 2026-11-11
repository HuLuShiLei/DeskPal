import { useState } from "preact/hooks";
import type { PanelState, PrivacyLevel, RoomItem } from "../bridge";
import { useHost } from "../host";

export function RoomPage({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);

  const create = async () => {
    const a = await ask({ title: "创建 Room", input: { placeholder: "Room 名称（1 到 30 个字符）", maxLength: 30, required: true }, okText: "创建" });
    if (a.ok) await run({ type: "room.create", name: a.text }, "已创建");
  };
  const join = async () => {
    const a = await ask({ title: "用邀请码加入 Room", input: { placeholder: "输入对方发给你的邀请码", maxLength: 20, required: true }, okText: "加入" });
    if (a.ok) await run({ type: "room.join", code: a.text }, "已加入");
  };

  return (
    <>
      <section class="card" aria-label="我的 Room">
        <h2>我的 Room</h2>
        {state.rooms.length === 0 && <p class="meta">还没有 Room。</p>}
        <ul class="room-list">
          {state.rooms.map((r) => (
            <li key={r.id}>
              <button class="room-item" aria-pressed={r.id === state.selectedRoomId} onClick={() => run({ type: "room.select", roomId: r.id })}>
                <span>
                  {r.name}
                  {r.isOwner && <span class="badge">所有者</span>}
                </span>
                <span class="meta">
                  {r.memberCount}/{r.maxMembers} 人
                </span>
              </button>
            </li>
          ))}
        </ul>
        <div class="actions">
          <button onClick={create}>创建 Room</button>
          <button onClick={join}>用邀请码加入</button>
        </div>
      </section>
      {room && <RoomDetail state={state} room={room} />}
      {room && state.privacy && <Privacy key={room.id + JSON.stringify(state.privacy.rows)} state={state} room={room} />}
    </>
  );
}

function RoomDetail({ state, room }: { state: PanelState; room: RoomItem }) {
  const { ask, run } = useHost();
  const others = state.members;

  const rename = async () => {
    const a = await ask({ title: "修改 Room 名称", input: { initial: room.name, maxLength: 30, required: true }, okText: "保存" });
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
  const leave = async () => {
    const a = await ask({ title: "退出 Room", message: `确定退出 Room「${room.name}」？`, okText: "退出", danger: true });
    if (a.ok) await run({ type: "room.leave", roomId: room.id }, "已退出");
  };
  const dissolve = async () => {
    const a = await ask({
      title: "解散 Room",
      message: `确定解散 Room「${room.name}」？成员关系和 Room 规则会一起删除，不能恢复。`,
      okText: "解散",
      danger: true,
    });
    if (a.ok) await run({ type: "room.dissolve", roomId: room.id }, "已解散");
  };

  return (
    <section class="card" aria-label={`Room「${room.name}」`}>
      <h2>Room「{room.name}」</h2>
      <ul class="members">
        <li>
          {state.me.displayName}（我）{room.isOwner && <span class="badge">所有者</span>}
        </li>
        {others.map((m) => (
          <li key={m.userId}>
            {m.name}
            {m.isOwner && <span class="badge">所有者</span>}
          </li>
        ))}
      </ul>
      <div class="actions">
        {room.isOwner && <button onClick={rename}>修改 Room 名称</button>}
        <button onClick={invite}>生成邀请码</button>
        {room.isOwner && others.length > 0 && <button onClick={transfer}>转让所有者</button>}
        {!room.isOwner && (
          <button class="danger" onClick={leave}>
            退出 Room
          </button>
        )}
        {room.isOwner && (
          <button class="danger" onClick={dissolve}>
            解散 Room
          </button>
        )}
      </div>
    </section>
  );
}

function Privacy({ state, room }: { state: PanelState; room: RoomItem }) {
  const { run } = useHost();
  const privacy = state.privacy!;
  const initial = Object.fromEntries(privacy.rows.map((r) => [r.category, r.level])) as Record<string, PrivacyLevel>;
  const [levels, setLevels] = useState(initial);
  const changed = privacy.rows.some((r) => levels[r.category] !== r.level);

  return (
    <section class="card" aria-label="隐私设置">
      <h2>隐私设置</h2>
      <p class="meta">设置 Room「{room.name}」的成员能看到你多少信息。只影响这个 Room。</p>
      <table class="privacy">
        <tbody>
          {privacy.rows.map((r) => (
            <tr key={r.category}>
              <th scope="row">{r.name}</th>
              <td>
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
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div class="actions">
        <button class="primary" disabled={!changed} onClick={() => run({ type: "room.privacy", roomId: room.id, levels }, "隐私设置已保存")}>
          保存隐私设置
        </button>
        {changed && <button onClick={() => setLevels(initial)}>撤销修改</button>}
      </div>
    </section>
  );
}