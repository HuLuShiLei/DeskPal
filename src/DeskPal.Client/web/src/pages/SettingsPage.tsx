import { useEffect, useRef, useState } from "preact/hooks";
import type { CloseAction, Language, PanelState, PluginCapability, PluginNote, PluginsView, PluginView, SignupInviteItem } from "../bridge";
import { useHost } from "../host";
import { hasMessage, t, type MessageKey } from "../i18n";
import { isLinux } from "../platform";
import { Avatar, Hint, Icon, Section, SettingRow, Switch } from "../ui";

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
      const a = await ask({ title: t("sharing.title"), message: t("sharing.details"), okText: t("sharing.agree") });
      if (!a.ok) return;
    }
    await run({ type: "settings.shareUsage", enabled }, enabled ? t("settings.sharingOn") : t("settings.sharingOff"));
  };

  return (
    <>
      <Section title={t("settings.account")}>
        <div class="card account-card">
          <Avatar name={state.me.displayName} tone={state.me.view.tone} />
          <div class="row-main">
            <p class="row-title">
              <span class="row-name">{state.me.displayName}</span>
              {s.isAdmin && <span class="badge">{t("settings.admin")}</span>}
              <button class="icon-button" title={t("settings.rename")} aria-label={t("settings.rename")} onClick={rename}>
                <Icon name="edit" />
              </button>
            </p>
            <p class="sub">{s.userName}</p>
          </div>
          <button class="subtle danger" onClick={logout}>
            {t("settings.logout")}
          </button>
        </div>
      </Section>
      <Section title={t("settings.general")}>
        <div class="card flush">
          <SettingRow title={t("settings.autoStart")}>
            <Switch checked={s.autoStart} label={t("settings.autoStart")} onChange={(v) => run({ type: "settings.autoStart", enabled: v })} />
          </SettingRow>
          {/* Linux 的 Wayland 不允许应用把自己的窗口置顶。 */}
          {!isLinux() && (
            <SettingRow title={t("settings.topmost")}>
              <Switch checked={s.topmost} label={t("settings.topmost")} onChange={(v) => run({ type: "settings.topmost", enabled: v })} />
            </SettingRow>
          )}
          <SettingRow title={t("settings.closeAction")}>
            <select
              aria-label={t("settings.closeAction")}
              value={s.closeAction}
              onChange={(e) => run({ type: "settings.closeAction", action: e.currentTarget.value as CloseAction })}
            >
              <option value="ask">{t("settings.closeAsk")}</option>
              <option value="minimize">{t("common.minimize")}</option>
              <option value="tray">{t("settings.closeTray")}</option>
              <option value="quit">{t("settings.closeQuit")}</option>
            </select>
          </SettingRow>
          <SettingRow title={t("settings.theme")}>
            <select aria-label={t("settings.theme")} value={s.theme} onChange={(e) => run({ type: "settings.theme", theme: e.currentTarget.value as "system" | "light" | "dark" })}>
              <option value="system">{t("settings.followSystem")}</option>
              <option value="light">{t("settings.light")}</option>
              <option value="dark">{t("settings.dark")}</option>
            </select>
          </SettingRow>
          <SettingRow title={t("settings.idle")} hint={t("settings.idleHint")}>
            <select
              aria-label={t("settings.idle")}
              value={s.idleMinutes}
              onChange={(e) => void run({ type: "settings.idleMinutes", minutes: Number(e.currentTarget.value) })}
            >
              {[1, 5, 10, 15, 30].map((minutes) => (
                <option key={minutes} value={minutes}>
                  {t("settings.minutes", { minutes })}
                </option>
              ))}
            </select>
          </SettingRow>
          <SettingRow title={t("settings.switchDelay")} hint={t("settings.switchDelayHint")}>
            <select
              aria-label={t("settings.switchDelay")}
              value={s.switchDelaySeconds}
              onChange={(e) => void run({ type: "settings.switchDelay", seconds: Number(e.currentTarget.value) })}
            >
              {[0, 3, 5, 10, 30].map((seconds) => (
                <option key={seconds} value={seconds}>
                  {seconds === 0 ? t("settings.switchNow") : t("settings.seconds", { seconds })}
                </option>
              ))}
            </select>
          </SettingRow>
          <SettingRow title={t("settings.language")}>
            <select aria-label={t("settings.language")} value={s.language} onChange={(e) => run({ type: "settings.language", language: e.currentTarget.value as Language })}>
              <option value="system">{t("settings.followSystem")}</option>
              <option value="zh-CN">{t("settings.languageChinese")}</option>
              <option value="en">{t("settings.languageEnglish")}</option>
            </select>
          </SettingRow>
        </div>
      </Section>
      <PersonalData personal={s.personal} />
      <Section title={t("settings.categories")}>
        <div class="card flush">
          <SettingRow title={t("sharing.title")} hint={t("settings.sharingHint")}>
            <Switch checked={s.shareUsage === true} label={t("sharing.title")} onChange={sharing} />
          </SettingRow>
        </div>
      </Section>
      <Section title={t("settings.about")}>
        <div class="card flush">
          <SettingRow title={t("settings.version", { version: s.version })} description={status ?? (s.updateReady ? t("settings.downloaded") : undefined)}>
            <button disabled={waiting} onClick={check}>
              {waiting ? t("common.pleaseWait") : t("settings.check")}
            </button>
          </SettingRow>
          {s.updateReady && (
            <SettingRow title={t("settings.ready")}>
              <button class="primary" onClick={() => run({ type: "update.restart" })}>
                {t("settings.restart")}
              </button>
            </SettingRow>
          )}
          <SettingRow title={t("settings.beta")} hint={t("settings.betaHint")}>
            <Switch checked={s.receiveBeta} label={t("settings.beta")} onChange={(v) => run({ type: "update.beta", enabled: v })} />
          </SettingRow>
          <SettingRow title={t("settings.issues")}>
            <button onClick={openIssuePage}>{t("settings.open")}</button>
          </SettingRow>
          <SettingRow title={t("settings.logs")}>
            <button onClick={() => run({ type: "logs.open" })}>{t("settings.open")}</button>
          </SettingRow>
        </div>
      </Section>
      {s.plugins && <PluginSettings plugins={s.plugins} />}
      {s.isAdmin && <SignupInvites />}
    </>
  );
}

const pluginGuideUrl = "https://github.com/HuLuShiLei/DeskPal/blob/main/docs/provider-guide.md";

const capabilityOrder: PluginCapability[] = ["foreground", "domain", "idle", "lock", "media"];

function pluginName(plugin: PluginView): string {
  const key = `plugins.officialName.${plugin.id.replace(/^official:/, "")}`;
  return plugin.official && hasMessage(key) ? t(key) : plugin.name;
}

function noteText(note: PluginNote): string {
  const key = `plugins.note.${note.key}`;
  return hasMessage(key) ? t(key, { text: note.text ?? "" }) : (note.text ?? note.key);
}

function capabilityName(capability: PluginCapability): string {
  return t(`plugins.cap.${capability}` as MessageKey);
}

/**
 * 插件列表：每个平台的内置插件（原来的采集器）和用户放进插件文件夹的插件放在一起，像 mod 列表一样排先后。
 * 每一项信息（当前窗口、网站、暂离、锁屏、媒体）取最上面一个开启的插件提供的；高亮的标签是正在使用的。
 */
function PluginSettings({ plugins }: { plugins: PluginsView }) {
  const { run, ask, toast } = useHost();
  const setEnabled = async (plugin: PluginView, enabled: boolean) => {
    if (enabled && !plugin.official) {
      const answer = await ask({
        title: t("plugins.enableTitle", { name: pluginName(plugin) }),
        message: t("plugins.enableMessage"),
        okText: t("plugins.enable"),
      });
      if (!answer.ok) return;
    }
    await run({ type: "plugins.setEnabled", id: plugin.id, enabled });
  };
  const action = async (plugin: PluginView, id: string) => {
    if (id === "linux.installGnomeExtension") {
      const answer = await ask({ title: t("plugins.installTitle"), message: t("plugins.installMessage"), okText: t("plugins.action.linux.installGnomeExtension") });
      if (!answer.ok) return;
    }
    const result = await run({ type: "plugins.action", id: plugin.id, action: id });
    const message = (result.data as { message?: string } | undefined)?.message;
    if (result.ok && message) toast(message);
  };
  const openGuide = () => {
    const link = document.createElement("a");
    link.href = pluginGuideUrl;
    link.target = "_blank";
    link.rel = "noreferrer";
    link.click();
  };
  const last = plugins.items.length - 1;
  return (
    <Section title={t("plugins.heading")} aside={<Hint text={t("plugins.hint")} />}>
      <div class="card flush plugin-list">
        {plugins.unprovided.length > 0 && (
          <div class="setting-row plugin-warning">
            <Icon name="warning" class="setting-icon" />
            <p class="setting-desc">{t("plugins.unprovided", { list: plugins.unprovided.map(capabilityName).join(t("common.listSeparator")) })}</p>
          </div>
        )}
        {plugins.items.map((plugin, index) => {
          const name = pluginName(plugin);
          const state = plugin.state === "running" ? null : t(`plugins.state.${plugin.state}`);
          const lines = [...(state ? [{ text: state, tone: plugin.state === "failed" ? "warn" : "info" }] : []), ...plugin.notes.map((n) => ({ text: noteText(n), tone: n.tone }))];
          return (
            <div class={plugin.enabled ? "setting-row plugin-row" : "setting-row plugin-row off"} key={plugin.id}>
              <div class="plugin-order">
                <button class="icon-button" aria-label={t("plugins.moveUp")} title={t("plugins.moveUp")} disabled={index === 0} onClick={() => run({ type: "plugins.move", id: plugin.id, offset: -1 })}>
                  <Icon name="chevronUp" />
                </button>
                <button class="icon-button" aria-label={t("plugins.moveDown")} title={t("plugins.moveDown")} disabled={index === last} onClick={() => run({ type: "plugins.move", id: plugin.id, offset: 1 })}>
                  <Icon name="chevronDown" />
                </button>
              </div>
              <div class="setting-text">
                <div class="setting-title">
                  <span>{name}</span>
                  {plugin.version && <span class="plugin-version">{plugin.version}</span>}
                </div>
                <div class="plugin-caps">
                  <span class={plugin.official ? "badge" : "badge muted"}>{plugin.official ? t("plugins.official") : t("plugins.user")}</span>
                  {capabilityOrder.filter((c) => plugin.provides.includes(c)).map((c) => {
                    const used = plugin.owns.includes(c);
                    return (
                      <span class={used ? "plugin-cap used" : "plugin-cap"} key={c} title={plugin.enabled ? t(used ? "plugins.capUsed" : "plugins.capOverridden", { cap: capabilityName(c) }) : undefined}>
                        {capabilityName(c)}
                      </span>
                    );
                  })}
                </div>
                {lines.map((line, i) => (
                  <p class={line.tone === "warn" ? "setting-desc plugin-note warn" : "setting-desc plugin-note"} key={i}>
                    {line.text}
                  </p>
                ))}
                {plugin.enabled && plugin.actions.length > 0 && (
                  <div class="plugin-actions">
                    {plugin.actions.map((id) => (
                      <button key={id} onClick={() => void action(plugin, id)}>
                        {hasMessage(`plugins.action.${id}`) ? t(`plugins.action.${id}` as MessageKey) : id}
                      </button>
                    ))}
                  </div>
                )}
              </div>
              <div class="setting-control plugin-control">
                <Switch checked={plugin.enabled} label={name} onChange={(v) => void setEnabled(plugin, v)} />
              </div>
            </div>
          );
        })}
        <SettingRow title={t("plugins.folder")} hint={t("plugins.folderHint")}>
          <button onClick={() => run({ type: "plugins.refresh" })}>{t("plugins.refresh")}</button>
          <button onClick={() => run({ type: "plugins.openFolder" })}>{t("settings.open")}</button>
        </SettingRow>
        <SettingRow title={t("plugins.guide")}>
          <button onClick={openGuide}>{t("settings.open")}</button>
        </SettingRow>
      </div>
    </Section>
  );
}

/** 个人分类、不记录标题和偏好设置除本机外保存到哪里，以及导入导出。 */
function PersonalData({ personal }: { personal: PanelState["settings"]["personal"] }) {
  const { run, ask, toast } = useHost();
  const [busy, setBusy] = useState(false);
  const accountSync = personal?.accountSync ?? false;
  const folder = personal?.folder ?? null;

  const guard = async (action: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  };

  const enable = async () => {
    const answer = await ask({ title: t("personal.enableTitle"), message: t("personal.enableMessage"), okText: t("sync.turnOn") });
    if (answer.ok) await guard(() => run({ type: "personal.sync.enable" }, t("personal.enabled")));
  };

  const disable = async () => {
    const answer = await ask({ title: t("personal.disableTitle"), message: t("personal.disableMessage"), okText: t("sync.disable"), danger: true });
    if (answer.ok) await guard(() => run({ type: "personal.sync.disable" }, t("sync.disabled")));
  };

  const chooseFolder = () => guard(() => run({ type: "personal.folder.choose" }));

  const stopFolder = async () => {
    const answer = await ask({ title: t("folder.stopTitle"), message: t("personal.folderStopMessage"), okText: t("folder.stop") });
    if (answer.ok) await run({ type: "personal.folder.stop" }, t("folder.stopped"));
  };

  const transfer = async (type: "personal.export" | "personal.import") => {
    const result = await run({ type });
    const message = (result.data as { message?: string } | undefined)?.message;
    if (result.ok && message) toast(message);
  };

  return (
    <Section title={t("personal.title")}>
      <div class="card flush">
        <SettingRow title={t("personal.sync")} hint={t("personal.syncHint")} description={folder && !accountSync ? t("sync.folderOn") : undefined}>
          <Switch
            checked={accountSync}
            label={t("personal.sync")}
            disabled={busy || (folder !== null && !accountSync)}
            onChange={(on) => void (on ? enable() : disable())}
          />
        </SettingRow>
        <SettingRow
          title={t("folder.title")}
          hint={t("personal.folderHint")}
          description={
            folder ? (
              <>
                {personal?.folderUnavailable && <span class="sub">{t("folder.unavailable")}</span>}
                <span class="sub folder-path" title={folder}>
                  {folder}
                </span>
              </>
            ) : accountSync ? (
              t("personal.accountOn")
            ) : undefined
          }
        >
          {folder ? (
            <button onClick={() => void stopFolder()}>{t("folder.stop")}</button>
          ) : (
            <button disabled={busy || accountSync} onClick={() => void chooseFolder()}>
              {t("folder.choose")}
            </button>
          )}
        </SettingRow>
        <SettingRow title={t("usage.transfer")} hint={t("personal.transferHint")}>
          <button onClick={() => void transfer("personal.export")}>{t("usage.export")}</button>
          <button onClick={() => void transfer("personal.import")}>{t("usage.import")}</button>
        </SettingRow>
      </div>
    </Section>
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
        <ul class="card flush list-card">
          {items.map((i) => (
            <li key={i.code} class={i.used ? "card-row used" : "card-row"}>
              <span class="kind-tile" aria-hidden="true">
                <Icon name="link" />
              </span>
              <div class="row-main">
                <p class="row-title invite-code selectable">{i.code}</p>
                <p class="sub">{i.text}</p>
              </div>
              {!i.used && (
                <>
                  <button class="icon-button" title={t("signup.copy")} aria-label={t("signup.copyName", { code: i.code })} onClick={() => run({ type: "signupInvites.copy", code: i.code }, t("signup.copied"))}>
                    <Icon name="copy" />
                  </button>
                  <button class="subtle danger" aria-label={t("signup.revokeName", { code: i.code })} onClick={() => revoke(i.code)}>
                    {t("signup.revoke")}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}