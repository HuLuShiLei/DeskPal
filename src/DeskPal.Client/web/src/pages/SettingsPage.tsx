import type { PanelState } from "../bridge";
import { useHost } from "../host";

export function SettingsPage({ state }: { state: PanelState }) {
  const { run } = useHost();
  const s = state.settings;

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
        <p class="meta">要换账号或服务器，从托盘菜单选择「退出登录」，再在登录页修改。</p>
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
        <div class="actions">
          <button onClick={() => run({ type: "update.check" })}>检查更新</button>
          <button class="primary" disabled={!s.updateReady} onClick={() => run({ type: "update.restart" })}>
            立即重启并更新
          </button>
        </div>
      </section>
    </>
  );
}