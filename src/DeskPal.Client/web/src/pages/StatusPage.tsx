import { useState } from "preact/hooks";
import type { MemberItem, PanelState } from "../bridge";
import { PresenceLines, useHost } from "../host";

export function StatusPage({ state }: { state: PanelState }) {
  return (
    <>
      {state.emptyHint && <p class="hint">{state.emptyHint}</p>}
      {state.members.map((m) => (
        <MemberCard key={m.userId} member={m} roomId={state.selectedRoomId!} />
      ))}
      <MyStatus state={state} />
      <ManualStatus state={state} />
    </>
  );
}

function MemberCard({ member, roomId }: { member: MemberItem; roomId: string }) {
  const { ask, run } = useHost();
  const poke = async () => {
    const a = await ask({
      title: `戳一下 ${member.name}`,
      input: { placeholder: "想说点什么？可以留空。", maxLength: 50 },
      okText: "发送",
    });
    if (a.ok) await run({ type: "member.poke", roomId, userId: member.userId, text: a.text || null }, "已发送");
  };

  return (
    <section class={member.online ? "card" : "card offline"} aria-label={member.name}>
      <div class="card-head">
        <h2>
          {member.name}
          {member.isOwner && <span class="badge">所有者</span>}
        </h2>
        <button onClick={poke} disabled={!member.online} title={member.online ? undefined : "对方离线，收不到"}>
          戳一下
        </button>
      </div>
      <PresenceLines view={member.view} />
      {member.meta && <p class="meta">{member.meta}</p>}
    </section>
  );
}

function MyStatus({ state }: { state: PanelState }) {
  const { ask, run, toast } = useHost();
  const { me, correction } = state;
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);

  const correct = async () => {
    if (!correction.target) {
      toast("还没有识别到当前使用的应用。切换到要纠正的应用，再打开面板。", true);
      return;
    }
    const a = await ask({
      title: "这条分错了",
      message: `把「${correction.target}」归类为：`,
      select: { options: correction.options, initial: correction.current },
      checkbox: room
        ? { label: `同步给 Room「${room.name}」的所有成员`, initial: true }
        : { label: "同步给 Room（当前没有 Room）", initial: false, disabled: true },
      okText: "保存",
    });
    if (a.ok) await run({ type: "presence.correct", category: a.selected, shareToRoom: a.checked }, "已保存，之后按新的类别识别");
  };

  return (
    <section class="card mine" aria-label="我的状态">
      <div class="card-head">
        <h2>我的状态</h2>
        {!me.paused && <button onClick={correct}>这条分错了</button>}
      </div>
      <PresenceLines view={me.view} />
      {me.source && <p class="meta">{me.source}</p>}
      {me.visible && <p class="meta">{me.visible}</p>}
      <label class="check">
        <input type="checkbox" checked={me.paused} onChange={(e) => run({ type: "presence.pause", paused: e.currentTarget.checked })} />
        暂停分享
      </label>
    </section>
  );
}

function ManualStatus({ state }: { state: PanelState }) {
  const { run } = useHost();
  const [draft, setDraft] = useState("");
  const current = state.me.manualText;

  return (
    <section class="card" aria-label="手动状态">
      <h2>手动状态</h2>
      {current ? <p>当前：{current}</p> : <p class="meta">设置后，Room 成员看到这段文字，而不是自动识别的结果。</p>}
      <div class="chips">
        {state.me.manualPresets.map((p) => (
          <button key={p} class="chip" aria-pressed={current === p} onClick={() => run({ type: "presence.manual", text: p })}>
            {p}
          </button>
        ))}
      </div>
      <form
        class="row"
        onSubmit={async (e) => {
          e.preventDefault();
          if (draft.trim() && (await run({ type: "presence.manual", text: draft.trim() })).ok) setDraft("");
        }}
      >
        <input aria-label="自定义手动状态" placeholder="自定义文字" maxLength={30} value={draft} onInput={(e) => setDraft(e.currentTarget.value)} />
        <button type="submit" disabled={!draft.trim()}>
          设置
        </button>
        {current && (
          <button type="button" onClick={() => run({ type: "presence.manual", text: null })}>
            清除
          </button>
        )}
      </form>
    </section>
  );
}