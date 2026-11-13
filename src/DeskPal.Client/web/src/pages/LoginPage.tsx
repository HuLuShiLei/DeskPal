import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge } from "../bridge";
import { t } from "../i18n";
import { Icon, TitleBar } from "../ui";

export function LoginPage({ bridge }: { bridge: Bridge }) {
  const [register, setRegister] = useState(false);
  const [serverUrl, setServerUrl] = useState("");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showServer, setShowServer] = useState(false);
  const userNameInput = useRef<HTMLInputElement>(null);
  const passwordInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    bridge.on((m) => {
      if (m.type === "login") {
        setServerUrl(m.serverUrl);
        setUserName(m.userName);
        setNotice(m.notice ?? null);
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
    if (!result.ok) setError(result.error ?? t("login.failed"));
  };

  return (
    <div class="window">
      <div class="chrome">
        <TitleBar bridge={bridge} title={register ? t("login.registerTitle") : t("login.title")} />
      </div>
      <form class="login" onSubmit={submit}>
        <div class="login-brand">
          <span class="app-mark large" aria-hidden="true">
            D
          </span>
          <h1>DeskPal</h1>
          <p class="hint-text">{t("login.tagline")}</p>
        </div>
        <div class="segmented" role="tablist">
          <button type="button" role="tab" aria-selected={!register} onClick={() => setRegister(false)}>
            {t("login.login")}
          </button>
          <button type="button" role="tab" aria-selected={register} onClick={() => setRegister(true)}>
            {t("login.register")}
          </button>
        </div>
        <label>
          {t("login.userName")}
          <input ref={userNameInput} value={userName} autoFocus onInput={(e) => setUserName(e.currentTarget.value)} required />
        </label>
        <label>
          {t("login.password")}
          <input ref={passwordInput} type="password" value={password} onInput={(e) => setPassword(e.currentTarget.value)} required />
        </label>
        {register && (
          <label>
            {t("login.displayName")}
            <input value={displayName} maxLength={20} onInput={(e) => setDisplayName(e.currentTarget.value)} required />
          </label>
        )}
        {showServer ? (
          <label>
            {t("login.server")}
            <input value={serverUrl} onInput={(e) => setServerUrl(e.currentTarget.value)} required />
          </label>
        ) : (
          <button type="button" class="link-button" onClick={() => setShowServer(true)}>
            {t("login.serverLink", { url: serverUrl || t("login.serverUnset") })}
          </button>
        )}
        {notice && !error && (
          <p class="infobar" role="status">
            <Icon name="info" />
            {notice}
          </p>
        )}
        {error && (
          <p class="infobar error" role="alert">
            <Icon name="warning" />
            {error}
          </p>
        )}
        <button type="submit" class="primary wide" disabled={busy}>
          {busy ? t("common.pleaseWait") : register ? t("login.register") : t("login.login")}
        </button>
      </form>
    </div>
  );
}
