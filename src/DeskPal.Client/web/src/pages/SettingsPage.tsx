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
  const [view, setView] = useState<"main" | "plugins">("main");

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

  const openPlugins = (open: boolean) => {
    setView(open ? "plugins" : "main");
    document.querySelector(".content")?.scrollTo({ top: 0 });
  };

  if (view === "plugins" && s.plugins) return <PluginsPage plugins={s.plugins} onBack={() => openPlugins(false)} />;

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
          {s.plugins && <PluginsEntry plugins={s.plugins} onOpen={() => openPlugins(true)} />}
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

      {s.isAdmin && <SignupInvites />}
    </>
  );
}

const pluginGuideUrl = "https://github.com/HuLuShiLei/DeskPal/blob/main/docs/provider-guide.md";

const capabilityOrder: PluginCapability[] = ["foreground", "domain", "idle", "lock", "media", "detail"];

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

function capabilityList(list: PluginCapability[]): string {
  return list.map(capabilityName).join(t("common.listSeparator"));
}

/** 折叠时第二行的一句话：没在运行时是状态，在运行时是正在提供的几项和补充标题的应用。 */
function pluginSummary(plugin: PluginView): string {
  if (plugin.state !== "running") return t(`plugins.state.${plugin.state}`);
  const parts: string[] = [];
  const exclusive = plugin.provides.filter((c) => c !== "detail");
  if (exclusive.length > 0) {
    const owned = capabilityOrder.filter((c) => c !== "detail" && plugin.owns.includes(c));
    parts.push(owned.length > 0 ? t("plugins.summary.owns", { list: capabilityList(owned) }) : t("plugins.summary.covered"));
  }
  if (plugin.provides.includes("detail")) {
    parts.push(plugin.apps.length > 0 ? t("plugins.summary.detail", { list: plugin.apps.join(t("common.listSeparator")) }) : t("plugins.summary.noApps"));
  }
  return parts.join(t("plugins.summary.separator"));
}

function needsAttention(plugin: PluginView): boolean {
  return plugin.enabled && (plugin.state === "failed" || plugin.notes.some((n) => n.tone === "warn"));
}

/** 设置页「通用」里进入插件页的一行：开启了几个；有信息没有插件提供时改为提示。 */
function PluginsEntry({ plugins, onOpen }: { plugins: PluginsView; onOpen: () => void }) {
  const on = plugins.items.filter((p) => p.enabled).length;
  const missing = plugins.unprovided.length > 0;
  const attention = missing || plugins.items.some(needsAttention);
  return (
    <button type="button" class="setting-row nav-row" onClick={onOpen}>
      <span class="setting-text">
        <span class="setting-title">
          <span>{t("plugins.heading")}</span>
        </span>
        <span class={missing ? "setting-desc nav-desc warn" : "setting-desc nav-desc"}>
          {missing ? t("plugins.unprovided", { list: capabilityList(plugins.unprovided) }) : t("plugins.entryDesc", { on, total: plugins.items.length })}
        </span>
      </span>
      <span class="nav-flag">{attention && <Icon name="warning" />}</span>
      <Icon name="forward" class="nav-arrow" />
    </button>
  );
}

/**
 * 插件页：每个平台的内置插件（原来的采集器）和用户放进插件文件夹的插件放在一起，像 mod 列表一样排先后。
 * 每一项信息（当前窗口、网站、暂离、锁屏、媒体）取最上面一个开启的插件提供的；补充标题按应用取最上面一个。
 * 每个插件折叠成固定两行，点开看提供的几项、适用的应用、说明和按钮；需要处理的插件一开始就展开。
 */
function PluginsPage({ plugins, onBack }: { plugins: PluginsView; onBack: () => void }) {
  const { run, ask, toast } = useHost();
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set(plugins.items.filter(needsAttention).map((p) => p.id)));
  const toggle = (id: string) =>
    setExpanded((now) => {
      const next = new Set(now);
      if (!next.delete(id)) next.add(id);
      return next;
    });
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
    <>
      <div class="subpage-head">
        <button class="icon-button" aria-label={t("plugins.back")} title={t("plugins.back")} onClick={onBack}>
          <Icon name="back" />
        </button>
        <h2>{t("plugins.heading")}</h2>
        <Hint text={t("plugins.hint")} />
        <span class="subpage-spacer" />
        <button class="icon-button" aria-label={t("plugins.refresh")} title={t("plugins.refresh")} onClick={() => run({ type: "plugins.refresh" })}>
          <Icon name="refresh" />
        </button>
      </div>
      <div class="plugin-toolbar">
        <button title={plugins.folder} onClick={() => run({ type: "plugins.openFolder" })}>
          {t("plugins.openFolder")}
        </button>
        <button title={t("plugins.guideTip")} onClick={openGuide}>
          {t("plugins.guide")}
          <Icon name="openInNew" />
        </button>
      </div>
      {plugins.unprovided.length > 0 && (
        <div class="card plugin-warning">
          <Icon name="warning" class="setting-icon" />
          <p class="setting-desc">{t("plugins.unprovided", { list: capabilityList(plugins.unprovided) })}</p>
        </div>
      )}
      <div class="card flush plugin-list">
        {plugins.items.map((plugin, index) => {
          const name = pluginName(plugin);
          const open = expanded.has(plugin.id);
          const summary = pluginSummary(plugin);
          const warn = needsAttention(plugin);
          return (
            <div class={["plugin-row", plugin.enabled ? "" : "off", open ? "open" : ""].join(" ").trim()} key={plugin.id}>
              <div class="plugin-head">
                <div class="plugin-order">
                  <button class="icon-button" aria-label={t("plugins.moveUp")} title={t("plugins.moveUp")} disabled={index === 0} onClick={() => run({ type: "plugins.move", id: plugin.id, offset: -1 })}>
                    <Icon name="chevronUp" />
                  </button>
                  <button class="icon-button" aria-label={t("plugins.moveDown")} title={t("plugins.moveDown")} disabled={index === last} onClick={() => run({ type: "plugins.move", id: plugin.id, offset: 1 })}>
                    <Icon name="chevronDown" />
                  </button>
                </div>
                <button type="button" class="plugin-main" tabIndex={-1} onClick={() => toggle(plugin.id)}>
                  <span class="plugin-line">
                    <span class="plugin-name" title={name}>
                      {name}
                    </span>
                    {plugin.version && <span class="plugin-version">{plugin.version}</span>}
                  </span>
                  <span class={plugin.enabled && plugin.state === "failed" ? "plugin-summary warn" : "plugin-summary"} title={summary}>
                    {summary}
                  </span>
                </button>
                <span class="plugin-flag">{warn && <Icon name="warning" />}</span>
                <button
                  class="icon-button plugin-expand"
                  aria-expanded={open}
                  aria-label={t(open ? "plugins.collapse" : "plugins.expand")}
                  title={t(open ? "plugins.collapse" : "plugins.expand")}
                  onClick={() => toggle(plugin.id)}
                >
                  <Icon name="info" />
                </button>
                <Switch checked={plugin.enabled} label={name} onChange={(v) => void setEnabled(plugin, v)} />
              </div>
              {open && (
                <div class="plugin-detail">
                  <div class="plugin-caps">
                    <span class={plugin.official ? "badge" : "badge muted"}>{plugin.official ? t("plugins.official") : t("plugins.user")}</span>
                    {capabilityOrder.filter((c) => plugin.provides.includes(c)).map((c) => {
                      const used = plugin.owns.includes(c);
                      const tip = used ? "plugins.capUsed" : c === "detail" ? "plugins.detailTip" : "plugins.capOverridden";
                      return (
                        <span class={used ? "plugin-cap used" : "plugin-cap"} key={c} title={plugin.enabled ? t(tip, { cap: capabilityName(c) }) : undefined}>
                          {capabilityName(c)}
                        </span>
                      );
                    })}
                  </div>
                  {plugin.provides.includes("detail") && plugin.apps.length > 0 && (
                    <p class="setting-desc plugin-note">{t("plugins.apps", { list: plugin.apps.join(t("common.listSeparator")) })}</p>
                  )}
                  {plugin.notes.map((note, i) => (
                    <p class={note.tone === "warn" ? "setting-desc plugin-note warn" : "setting-desc plugin-note"} key={i}>
                      {noteText(note)}
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
              )}
            </div>
          );
        })}
      </div>
      <p class="plugin-footnote">{t("plugins.folderNote")}</p>
    </>
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