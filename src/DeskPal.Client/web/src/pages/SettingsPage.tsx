import { useEffect, useRef, useState } from "preact/hooks";
import type { Language, PanelState, SignupInviteItem } from "../bridge";
import { useHost } from "../host";
import { t } from "../i18n";
import { Icon, Section, SettingRow, Switch } from "../ui";

const issuePageUrl = "https://github.com/HuLuShiLei/DeskPal/issues/new/choose";

function openIssuePage() {
  const link = document.createElement("a");
  link.href = issuePageUrl;
  link.target = "_blank";
  link.rel = "noreferrer";
  link.click();
}

export function SettingsPage({ state }: { state: PanelState }) {
  const { run, ask } = useHost();
  const s = state.settings;
  const [pressed, setPressed] = useState(false);
  const seen = useRef(false);
  if (s.updateActivity) seen.current = true;
  const status = s.updateActivity ?? (pressed && !seen.current ? t("settings.checking") : null);
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

  const rename = async () => {
    const a = await ask({
      title: t("settings.rename"),
      message: t("settings.renameMessage"),
      input: { initial: state.me.displayName, placeholder: t("settings.renamePlaceholder"), maxLength: 20, required: true },
      okText: t("common.save"),
    });
    if (a.ok && a.text !== state.me.displayName) await run({ type: "me.rename", name: a.text }, t("common.renamed"));
  };

  const logout = async () => {
    const answer = await ask({
      title: t("settings.logout"),
      message: t("settings.logoutMessage"),
      okText: t("settings.logout"),
      danger: true,
    });
    if (answer.ok) await run({ type: "auth.logout" });
  };

  const sharing = async (enabled: boolean) => {
    if (enabled) {
      const a = await ask({ title: t("sharing.title"), message: t("sharing.message"), okText: t("sharing.agree") });
      if (!a.ok) return;
    }
    await run({ type: "settings.shareUsage", enabled }, enabled ? t("settings.sharingOn") : t("settings.sharingOff"));
  };

  return (
    <>
      <Section title={t("settings.account")}>
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
            <button class="icon-button" title={t("settings.rename")} aria-label={t("settings.rename")} onClick={rename}>
              <Icon name="edit" />
            </button>
            <button class="subtle danger" onClick={logout}>
              {t("settings.logout")}
            </button>
          </SettingRow>
        </div>
      </Section>
      <Section title={t("settings.general")}>
        <div class="card flush">
          <SettingRow title={t("settings.autoStart")} hint={t("settings.autoStartHint")}>
            <Switch checked={s.autoStart} label={t("settings.autoStart")} onChange={(v) => run({ type: "settings.autoStart", enabled: v })} />
          </SettingRow>
          <SettingRow title={t("settings.topmost")} hint={t("settings.topmostHint")}>
            <Switch checked={s.topmost} label={t("settings.topmost")} onChange={(v) => run({ type: "settings.topmost", enabled: v })} />
          </SettingRow>
          <SettingRow title={t("settings.closeAction")} hint={t("settings.closeActionHint")}>
            <select
              aria-label={t("settings.closeAction")}
              value={s.closeAction}
              onChange={(e) => run({ type: "settings.closeAction", action: e.currentTarget.value as "ask" | "minimize" | "tray" })}
            >
              <option value="ask">{t("settings.closeAsk")}</option>
              <option value="minimize">{t("common.minimize")}</option>
              <option value="tray">{t("settings.closeTray")}</option>
            </select>
          </SettingRow>
          <SettingRow title={t("settings.theme")} hint={t("settings.themeHint")}>
            <select aria-label={t("settings.theme")} value={s.theme} onChange={(e) => run({ type: "settings.theme", theme: e.currentTarget.value as "system" | "light" | "dark" })}>
              <option value="system">{t("settings.followSystem")}</option>
              <option value="light">{t("settings.light")}</option>
              <option value="dark">{t("settings.dark")}</option>
            </select>
          </SettingRow>
          <SettingRow title={t("settings.language")} hint={t("settings.languageHint")}>
            <select aria-label={t("settings.language")} value={s.language} onChange={(e) => run({ type: "settings.language", language: e.currentTarget.value as Language })}>
              <option value="system">{t("settings.followSystem")}</option>
              <option value="zh-CN">{t("settings.languageChinese")}</option>
              <option value="en">{t("settings.languageEnglish")}</option>
            </select>
          </SettingRow>
        </div>
      </Section>
      <Section title={t("settings.categories")}>
        <div class="card flush">
          <SettingRow title={t("sharing.title")} hint={t("settings.sharingHint")}>
            <Switch checked={s.shareUsage === true} label={t("sharing.title")} onChange={sharing} />
          </SettingRow>
        </div>
      </Section>
      <Section title={t("settings.updates")}>
        <div class="card flush">
          <SettingRow title={t("settings.version", { version: s.version })} description={status ?? (s.updateReady ? t("settings.downloaded") : undefined)}>
            <button disabled={waiting} onClick={check}>
              {waiting ? t("common.pleaseWait") : t("settings.check")}
            </button>
          </SettingRow>
          <SettingRow title={t("settings.beta")} hint={t("settings.betaHint")}>
            <Switch checked={s.receiveBeta} label={t("settings.beta")} onChange={(v) => run({ type: "update.beta", enabled: v })} />
          </SettingRow>
          {s.updateReady && (
            <SettingRow title={t("settings.ready")}>
              <button class="primary" onClick={() => run({ type: "update.restart" })}>
                {t("settings.restart")}
              </button>
            </SettingRow>
          )}
        </div>
      </Section>
      <Section title={t("settings.feedback")}>
        <div class="card flush">
          <SettingRow title={t("settings.issues")} hint={t("settings.issuesHint")}>
            <button onClick={openIssuePage}>{t("settings.open")}</button>
          </SettingRow>
        </div>
      </Section>
      {s.isAdmin && <SignupInvites />}
    </>
  );
}

/** 只有管理员能看到。列表不在 PanelState 里，打开设置页时向宿主要一次，生成和作废后用返回的新列表。 */
function SignupInvites() {
  const { run, ask } = useHost();
  const [items, setItems] = useState<SignupInviteItem[] | null>(null);

  useEffect(() => {
    void run({ type: "signupInvites.list" }).then((r) => r.ok && setItems(r.data as SignupInviteItem[]));
  }, []);

  const create = async () => {
    const r = await run({ type: "signupInvites.create" });
    const data = r.data as { code: string; message: string; items: SignupInviteItem[] } | undefined;
    if (!r.ok || !data) return;
    setItems(data.items);
    await ask({ title: t("signup.createdTitle"), highlight: data.code, message: data.message, okText: t("common.gotIt"), cancelText: null });
  };

  const revoke = async (code: string) => {
    const a = await ask({ title: t("signup.revokeTitle"), message: t("signup.revokeMessage", { code }), okText: t("signup.revoke"), danger: true });
    if (!a.ok) return;
    const r = await run({ type: "signupInvites.revoke", code }, t("signup.revoked"));
    if (r.ok) setItems(r.data as SignupInviteItem[]);
  };

  return (
    <Section
      title={t("signup.title")}
      aside={
        <div class="section-actions">
          <button onClick={create}>
            <Icon name="add" />
            {t("signup.create")}
          </button>
        </div>
      }
    >
      {items && items.length === 0 && (
        <p class="infobar">
          <Icon name="info" />
          {t("signup.empty")}
        </p>
      )}
      {items && items.length > 0 && (
        <ul class="list">
          {items.map((i) => (
            <li key={i.code} class="list-row compact">
              <div class="row-main">
                <p class="row-title">
                  <button type="button" class="code-copy" title={t("signup.copy")} aria-label={t("signup.copyName", { code: i.code })} onClick={() => run({ type: "signupInvites.copy", code: i.code }, t("signup.copied"))}>{i.code}</button>
                </p>
                <p class="sub">{i.text}</p>
              </div>
              {!i.used && (
                <button class="subtle danger" aria-label={t("signup.revokeName", { code: i.code })} onClick={() => revoke(i.code)}>
                  {t("signup.revoke")}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}