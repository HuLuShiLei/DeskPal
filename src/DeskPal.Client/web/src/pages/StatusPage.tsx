import type { MemberItem, PanelState, UnclassifiedItem } from "../bridge";
import { PresenceDetail, StatusBadge, useCountdown, useHost } from "../host";
import { Avatar, Hint, HoverTip, Icon, Section, Switch } from "../ui";

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
              <MemberRow key={m.userId} member={m} />
            ))}
          </ul>
        )}
      </Section>
      {state.unclassified.length > 0 && <Unclassified state={state} />}
    </>
  );
}

function MemberRow({ member }: { member: MemberItem }) {
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
  const label = member.pinned ? `取消置顶${member.name}` : `把${member.name}置顶到桌面`;
  return (
    <li class={member.online ? "list-row member" : "list-row member offline"} aria-label={member.name}>
      <Avatar name={member.name} tone={member.view.tone} />
      <div class="row-main">
        <p class="row-title">
          {member.name}
          {member.isOwner && <span class="badge">所有者</span>}
          {member.meta && <span class="row-meta">{member.meta}</span>}
        </p>
        <div>
          <StatusBadge view={member.view} />
        </div>
        <PresenceDetail view={member.view} />
      </div>
      <button
        class={member.pinned ? "icon-button pin-button active" : "icon-button pin-button"}
        title={member.pinned ? "取消置顶" : "置顶到桌面"}
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
      toast("还没有识别到当前使用的应用。切换到要纠正的应用，再打开面板。", true);
      return;
    }
    const a = await ask({
      title: "纠正分类",
      message: `把「${correction.target}」归为所选类别。`,
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
      <div class={`card me tv-${me.view.tone}`}>
        <div class="me-head">
          <Avatar name={me.displayName} tone={me.view.tone} />
          <div class="row-main">
            <div class="row-title">
              <span class="row-name">{me.displayName}（我）</span>
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

/** 新状态的倒计时放在标题行右侧，进度条贴在卡片底边，出现和消失都不改变卡片高度。标题行窄时「秒后变为」缩成箭头，仍是一行。 */
function Pending({ pending, source }: { pending: NonNullable<PanelState["me"]["pending"]>; source: string | null }) {
  const left = useCountdown(pending.remainingMs);
  const seconds = Math.ceil(left / 1000);
  const progress = Math.min(1, Math.max(0, 1 - left / (pending.totalSeconds * 1000)));
  const why = [source, `保持 ${pending.totalSeconds} 秒不变后同步给房间成员`].filter(Boolean).join("\n");
  return (
    <HoverTip text={pending.full ? `${pending.full}\n\n${why}` : why} class={`pending tv-${pending.view.tone}`}>
      <span class="pending-text" role="status" aria-label={seconds > 0 ? `${seconds} 秒后变更为${pending.view.headline}` : `即将变更为${pending.view.headline}`}>
        <Icon name="timer" class="pending-icon" />
        {seconds > 0 ? (
          <>
            <b>{seconds}</b> 秒<span class="pending-verb">后变为</span>
          </>
        ) : (
          <>
            即将<span class="pending-verb">变为</span>
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
              <p class="row-title"><span class="row-name" title={item.name}>{item.name}</span></p>
              <p class="sub">{item.today}</p>
            </div>
            <button onClick={() => classify(item)}>归类</button>
          </li>
        ))}
      </ul>
    </Section>
  );
}
