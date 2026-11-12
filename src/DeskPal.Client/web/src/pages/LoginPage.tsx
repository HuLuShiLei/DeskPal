import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge } from "../bridge";
import { Icon, TitleBar } from "../ui";

export function LoginPage({ bridge }: { bridge: Bridge }) {
  const [register, setRegister] = useState(false);
  const [serverUrl, setServerUrl] = useState("");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showServer, setShowServer] = useState(false);
  const userNameInput = useRef<HTMLInputElement>(null);
  const passwordInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    bridge.on((m) => {
      if (m.type === "login") {
        setServerUrl(m.serverUrl);
        setUserName(m.userName);
        (m.userName ? passwordInput : userNameInput).current?.focus();
      }
    });
    bridge.notify({ type: "ready" });
  }, []);

  const submit = async (e: Event) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const result = await bridge.request(
      register
        ? { type: "auth.register", serverUrl, userName, password, displayName }
        : { type: "auth.login", serverUrl, userName, password },
    );
    setBusy(false);
    if (!result.ok) setError(result.error ?? "登录失败");
  };

  return (
    <div class="window">
      <div class="chrome">
        <TitleBar bridge={bridge} title={register ? "注册 DeskPal" : "登录 DeskPal"} />
      </div>
      <form class="login" onSubmit={submit}>
        <div class="login-brand">
          <span class="app-mark large" aria-hidden="true">
            D
          </span>
          <h1>DeskPal</h1>
          <p class="hint-text">和房间里的人互相看到在忙什么</p>
        </div>
        <div class="segmented" role="tablist">
          <button type="button" role="tab" aria-selected={!register} onClick={() => setRegister(false)}>
            登录
          </button>
          <button type="button" role="tab" aria-selected={register} onClick={() => setRegister(true)}>
            注册
          </button>
        </div>
        <label>
          用户名
          <input ref={userNameInput} value={userName} autoFocus onInput={(e) => setUserName(e.currentTarget.value)} required />
        </label>
        <label>
          密码
          <input ref={passwordInput} type="password" value={password} onInput={(e) => setPassword(e.currentTarget.value)} required />
        </label>
        {register && (
          <label>
            显示名称
            <input value={displayName} maxLength={20} onInput={(e) => setDisplayName(e.currentTarget.value)} required />
          </label>
        )}
        {showServer ? (
          <label>
            服务器地址
            <input value={serverUrl} onInput={(e) => setServerUrl(e.currentTarget.value)} required />
          </label>
        ) : (
          <button type="button" class="link-button" onClick={() => setShowServer(true)}>
            服务器：{serverUrl || "未设置"}（修改）
          </button>
        )}
        {error && (
          <p class="infobar error" role="alert">
            <Icon name="warning" />
            {error}
          </p>
        )}
        <button type="submit" class="primary wide" disabled={busy}>
          {busy ? "请稍候…" : register ? "注册" : "登录"}
        </button>
      </form>
    </div>
  );
}
