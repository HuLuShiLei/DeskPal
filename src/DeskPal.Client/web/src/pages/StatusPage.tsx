import { useState } from "preact/hooks";
import type { MemberItem, PanelState, UnclassifiedItem } from "../bridge";
import { PresenceLines, useHost } from "../host";
import { Avatar, Icon, Section, Switch } from "../ui";

export function StatusPage({ state }: { state: PanelState }) {
  const room = state.rooms.find((r) => r.id === state.selectedRoomId);
  return (
    <>
      <MyStatus state={state} />
      <Section title={room ? `房间成员（${state.members.length}）` : "房间成员"} label="房间成员">
        {state.emptyHint && (
          <p class="infobar">
            <Icon name="info" />
            {state.emptyHint}
          </p>
        )}
        {state.members.length > 0 && (
          <ul class="list">
            {state.members.map((m) => (
              <MemberRow key={m.userId} member={m} roomId={state.selectedRoomId!} />
            ))}
          </ul>
        )}
      </Section>
      {state.unclassified.length > 0 && <Unclassified state={state} />}
      <ManualStatus state={state} />
    </>
  );
}

function MemberRow({ member, roomId }: { member: MemberItem; roomId: string }) {
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
    <li class={member.online ? "list-row member" : "list-row member offline"} aria-label={member.name}>
      <Avatar name={member.name} tone={member.view.tone} />
      <div class="row-main">
        <p class="row-title">
          {member.name}
          {member.isOwner && <span class="badge">所有者</span>}
          {member.meta && <span class="row-meta">{member.meta}</span>}
        </p>
        <PresenceLines view={member.view} />
      </div>
      <button class="icon-button" onClick={poke} disabled={!member.online} title={member.online ? "戳一下" : "对方离线，收不到"} aria-label={`戳一下${member.name}`}>
        <Icon name="ring" />
      </button>
    </li>
  );
}

function MyStatus({ state }: { state: PanelState }) {
  const { ask, run, toast } = useHost();
  const { me, correction } = state;

  const correct = async () => {
    if (!correction.target) {
      toast("还没有识别到当前使用的应用。切换到要纠正的应用，再打开面板。", true);
      return;
    }
    const a = await ask({
      title: "纠正分类",
      message: `把「${correction.target}」归为所选类别。只改变你自己的识别结果，所有房间和电脑通用，房间里的其他人不受影响。`,
      select: { options: correction.options, initial: correction.current },
      okText: "保存",
    });
    if (a.ok) await run({ type: "presence.correct", category: a.selected }, "已保存，所有房间通用");
  };

  const reset = async () => {
    const a = await ask({ title: "恢复默认分类", message: `删除你对「${correction.target}」设置的分类，改用默认分类。`, okText: "恢复默认" });
    if (a.ok) await run({ type: "presence.resetCorrection" }, "已恢复默认分类");
  };

  return (
    <Section label="我的状态">
      <div class="card me">
        <div class="me-head">
          <Avatar name={me.displayName} tone={me.view.tone} />
          <div class="row-main">
            <p class="row-title">{me.displayName}（我）</p>
            <PresenceLines view={me.view} />
          </div>
        </div>
        {(me.source || me.visible) && (
          <dl class="facts">
            {me.source && (
              <>
                <dt>识别方式</dt>
                <dd>{me.source}</dd>
              </>
            )}
            {me.visible && (
              <>
                <dt>房间成员看到</dt>
                <dd>{me.visible}</dd>
              </>
            )}
          </dl>
        )}
        <div class="toolbar">
          {!me.paused && (
            <button onClick={correct}>
              <Icon name="tag" />
              纠正分类
            </button>
          )}
          {!me.paused && correction.personal && (
            <button onClick={reset}>
              <Icon name="undo" />
              恢复默认
            </button>
          )}
          <span class="spacer" />
          <label class="inline-switch">
            暂停分享
            <Switch checked={me.paused} label="暂停分享" onChange={(v) => run({ type: "presence.pause", paused: v })} />
          </label>
        </div>
      </div>
    </Section>
  );
}

function ManualStatus({ state }: { state: PanelState }) {
  const { run } = useHost();
  const { me } = state;
  const [draft, setDraft] = useState("");
  const custom = async () => {
    const text = draft.trim();
    if (text && (await run({ type: "presence.manual", text })).ok) setDraft("");
  };

  return (
    <Section title="手动状态">
      <div class="card">
        <p class="hint-text">{me.manualText ? `当前：${me.manualText}` : "设置后，房间成员看到这段文字，而不是自动识别的结果。"}</p>
        <div class="chips">
          {me.manualPresets.map((p) => (
            <button key={p} class="chip" aria-pressed={me.manualText === p} onClick={() => run({ type: "presence.manual", text: me.manualText === p ? null : p })}>
              {p}
            </button>
          ))}
        </div>
        <form
          class="inline-form"
          onSubmit={(e) => {
            e.preventDefault();
            void custom();
          }}
        >
          <input aria-label="自定义手动状态" placeholder="自定义文字" maxLength={30} value={draft} onInput={(e) => setDraft(e.currentTarget.value)} />
          <button type="submit" disabled={!draft.trim()}>
            设置
          </button>
          {me.manualText && (
            <button type="button" onClick={() => run({ type: "presence.manual", text: null })}>
              清除
            </button>
          )}
        </form>
      </div>
    </Section>
  );
}

function Unclassified({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const classify = async (item: UnclassifiedItem) => {
    const a = await ask({
      title: `「${item.name}」算什么？`,
      message: "只改变你自己的识别结果，所有房间和电脑通用。",
      select: { options: state.correction.options, initial: "Work" },
      okText: "保存",
    });
    if (a.ok) await run({ type: "rules.set", kind: item.kind, pattern: item.pattern, name: item.name, category: a.selected }, "已保存");
  };

  return (
    <Section title="待归类" aside={<span class="section-note">今天用过、还没有识别的应用</span>}>
      <ul class="list">
        {state.unclassified.map((item) => (
          <li key={item.pattern} class="list-row compact">
            <div class="row-main">
              <p class="row-title">{item.name}</p>
              <p class="sub">{item.today}</p>
            </div>
            <button onClick={() => classify(item)}>归类</button>
          </li>
        ))}
      </ul>
    </Section>
  );
}
