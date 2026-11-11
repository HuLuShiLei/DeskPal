import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge } from "../bridge";

export function LoginPage({ bridge }: { bridge: Bridge }) {
  const [register, setRegister] = useState(false);
  const [serverUrl, setServerUrl] = useState("");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
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
    <form class="login" onSubmit={submit}>
      <h1>DeskPal</h1>
      <nav class="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={!register} onClick={() => setRegister(false)}>
          登录
        </button>
        <button type="button" role="tab" aria-selected={register} onClick={() => setRegister(true)}>
          注册
        </button>
      </nav>
      <label>
        服务器地址
        <input value={serverUrl} onInput={(e) => setServerUrl(e.currentTarget.value)} required />
      </label>
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
          <input value={displayName} maxLength={30} onInput={(e) => setDisplayName(e.currentTarget.value)} required />
        </label>
      )}
      {error && (
        <p class="error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" class="primary" disabled={busy}>
        {busy ? "请稍候…" : register ? "注册" : "登录"}
      </button>
    </form>
  );
}