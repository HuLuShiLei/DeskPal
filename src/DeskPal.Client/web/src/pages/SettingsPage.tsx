import { useRef, useState } from "preact/hooks";
import type { PanelState } from "../bridge";
import { useHost } from "../host";

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

  return (
    <>
      <section class="card" aria-label="账号">
        <h2>账号</h2>
        <dl class="fields">
          <dt>显示名称</dt>
          <dd>{state.me.displayName}</dd>
          <dt>用户名</dt>
          <dd>{s.userName}</dd>
          <dt>服务器</dt>
          <dd>{s.serverUrl}</dd>
        </dl>
        <p class="meta">退出后会回到登录页，可以换账号或服务器。</p>
        <div class="actions">
          <button class="danger" onClick={logout}>
            退出登录
          </button>
        </div>
      </section>
      <section class="card" aria-label="常规">
        <h2>常规</h2>
        <label class="check">
          <input type="checkbox" checked={s.autoStart} onChange={(e) => run({ type: "settings.autoStart", enabled: e.currentTarget.checked })} />
          开机自启
        </label>
        <label class="check">
          <input type="checkbox" checked={s.topmost} onChange={(e) => run({ type: "settings.topmost", enabled: e.currentTarget.checked })} />
          窗口置顶
        </label>
      </section>
      <section class="card" aria-label="更新">
        <h2>更新</h2>
        <p>当前版本：{s.version}</p>
        <label class="check">
          <input type="checkbox" checked={s.receiveBeta} onChange={(e) => run({ type: "update.beta", enabled: e.currentTarget.checked })} />
          加入测试版
        </label>
        {status && (
          <p class="status" role="status">
            {status}
          </p>
        )}
        <div class="actions">
          <button disabled={waiting} onClick={check}>
            {waiting ? "请稍候…" : "检查更新"}
          </button>
          <button class="primary" disabled={!s.updateReady} onClick={() => run({ type: "update.restart" })}>
            立即重启并更新
          </button>
        </div>
      </section>
    </>
  );
}
