import { useRef, useState } from "preact/hooks";
import type { MyRuleItem, PanelState } from "../bridge";
import { useHost } from "../host";
import { sharingMessage } from "../sharing";
import { Icon, Section, SettingRow, Switch } from "../ui";

export function SettingsPage({ state }: { state: PanelState }) {
  const { run, ask } = useHost();
  const s = state.settings;
  const [pressed, setPressed] = useState(false);
  const seen = useRef(false);
  if (s.updateActivity) seen.current = true;
  const status = s.updateActivity ?? (pressed && !seen.current ? "正在检查更新…" : null);
  const waiting = pressed || s.updateActivity !== null;

  const check = async () => {
    seen.current = false;
    setPressed(true);
    try {
      await run({ type: "update.check" });
    } finally {
      setPressed(false);
      seen.current = false;
    }
  };

  const logout = async () => {
    const answer = await ask({
      title: "退出登录",
      message: "确定退出登录？之后会回到登录页，可以换账号或服务器。",
      okText: "退出登录",
      danger: true,
    });
    if (answer.ok) await run({ type: "auth.logout" });
  };

  const sharing = async (enabled: boolean) => {
    if (enabled) {
      const a = await ask({ title: "帮助改进默认分类", message: sharingMessage, okText: "同意分享" });
      if (!a.ok) return;
    }
    await run({ type: "settings.shareUsage", enabled }, enabled ? "已开启" : "已关闭，已上传的未识别应用记录会删除");
  };

  return (
    <>
      <Section title="账号">
        <div class="card flush">
          <SettingRow title={state.me.displayName} description={
              <>
                {s.userName}
                <span class="sub" title={s.serverUrl}>
                  {s.serverUrl}
                </span>
              </>
            }
          >
            <button class="subtle danger" onClick={logout}>
              退出登录
            </button>
          </SettingRow>
        </div>
      </Section>
      <Section title="常规">
        <div class="card flush">
          <SettingRow title="开机自启" description="登录 Windows 后在后台启动">
            <Switch checked={s.autoStart} label="开机自启" onChange={(v) => run({ type: "settings.autoStart", enabled: v })} />
          </SettingRow>
          <SettingRow title="窗口置顶" description="面板显示在其他窗口上面">
            <Switch checked={s.topmost} label="窗口置顶" onChange={(v) => run({ type: "settings.topmost", enabled: v })} />
          </SettingRow>
        </div>
      </Section>
      <Section title="分类">
        <div class="card flush">
          <SettingRow title="帮助改进默认分类" description="上传没有识别的桌面应用和你的分类，不含窗口标题和网页">
            <Switch checked={s.shareUsage === true} label="帮助改进默认分类" onChange={sharing} />
          </SettingRow>
        </div>
        <MyRules state={state} />
      </Section>
      <Section title="更新">
        <div class="card flush">
          <SettingRow title={`当前版本 ${s.version}`} description={status ?? (s.updateReady ? "新版本已下载，重启后安装" : undefined)}>
            <button disabled={waiting} onClick={check}>
              {waiting ? "请稍候…" : "检查更新"}
            </button>
          </SettingRow>
          <SettingRow title="加入测试版" description="更早收到新功能，可能不稳定">
            <Switch checked={s.receiveBeta} label="加入测试版" onChange={(v) => run({ type: "update.beta", enabled: v })} />
          </SettingRow>
          {s.updateReady && (
            <SettingRow title="新版本已就绪">
              <button class="primary" onClick={() => run({ type: "update.restart" })}>
                立即重启并更新
              </button>
            </SettingRow>
          )}
        </div>
      </Section>
    </>
  );
}

function MyRules({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const edit = async (rule: MyRuleItem) => {
    const a = await ask({
      title: `「${rule.name}」算什么？`,
      select: { options: state.correction.options, initial: rule.category },
      okText: "保存",
    });
    if (a.ok && a.selected !== rule.category) {
      await run({ type: "rules.set", kind: rule.kind, pattern: rule.pattern, name: rule.name, category: a.selected }, "已保存");
    }
  };
  const remove = async (rule: MyRuleItem) => {
    const a = await ask({ title: "删除分类", message: `删除你对「${rule.name}」设置的分类，改用默认分类。`, okText: "删除", danger: true });
    if (a.ok) await run({ type: "rules.delete", kind: rule.kind, pattern: rule.pattern }, "已删除");
  };

  return (
    <div class="card flush">
      <SettingRow title="我的分类" description="在状态页「纠正分类」后出现在这里。所有房间和电脑通用，只影响你自己。" />
      {state.myRules.length === 0 ? (
        <p class="card-note">还没有设置过。</p>
      ) : (
        <ul class="list inset">
          {state.myRules.map((r) => (
            <li key={r.kind + r.pattern} class="list-row compact">
              <div class="row-main">
                <p class="row-title">{r.name}</p>
                <p class="sub">
                  {r.kindName} · {r.pattern}
                </p>
              </div>
              <span class="pill">{r.categoryName}</span>
              <button class="icon-button" title="修改" aria-label={`修改${r.name}的分类`} onClick={() => edit(r)}>
                <Icon name="edit" />
              </button>
              <button class="icon-button" title="删除" aria-label={`删除${r.name}的分类`} onClick={() => remove(r)}>
                <Icon name="delete" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
