import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge, CloseAction, PanelState } from "./bridge";
import { HostContext, useHost, useHostServices } from "./host";
import { RoomPage } from "./pages/RoomPage";
import { RulesPage } from "./pages/RulesPage";
import { SettingsPage } from "./pages/SettingsPage";
import { StatsPage, type StatsTarget } from "./pages/StatsPage";
import { StatusPage } from "./pages/StatusPage";
import { setLocale, t, type MessageKey } from "./i18n";
import { askSharing } from "./sharing";
import { applyTheme } from "./theme";
import { Flyout, Icon, RoomBadge, TitleBar, type IconName } from "./ui";

const tabs: { id: Tab; name: MessageKey; icon: IconName }[] = [
  { id: "status", name: "tab.status", icon: "home" },
  { id: "stats", name: "tab.stats", icon: "chart" },
  { id: "room", name: "tab.room", icon: "group" },
  { id: "rules", name: "tab.rules", icon: "filter" },
  { id: "settings", name: "tab.settings", icon: "settings" },
];

type Tab = "status" | "stats" | "room" | "rules" | "settings";

export function App({ bridge }: { bridge: Bridge }) {
  const [state, setState] = useState<PanelState | null>(null);
  const [tab, setTab] = useState<Tab>("status");
  const [statsTarget, setStatsTarget] = useState<StatsTarget>(null);
  const [closeAction, setCloseAction] = useState<CloseAction>("ask");
  const { host, overlay, dialogOpen } = useHostServices(bridge);
  const askedSharing = useRef(false);
  const shownWhatsNew = useRef(false);
  const closing = useRef(false);

  const performClose = (action: Exclude<CloseAction, "ask">) => {
    if (action === "minimize") bridge.notify({ type: "window.minimize" });
    else if (action === "quit") bridge.notify({ type: "window.quit" });
    else bridge.notify({ type: "window.close" });
  };

  const closePanel = async () => {
    if (closing.current) return;
    closing.current = true;
    try {
      if (closeAction === "minimize" || closeAction === "tray" || closeAction === "quit") {
        performClose(closeAction);
        return;
      }
      const answer = await host.ask({
        title: t("app.closeTitle"),
        message: t("app.closeMessage"),
        select: {
          presentation: "radio",
          initial: "tray",
          options: [
            { value: "minimize", name: t("common.minimize") },
            { value: "tray", name: t("settings.closeTray") },
            { value: "quit", name: t("settings.closeQuit") },
          ],
        },
        checkbox: { label: t("app.dontAskAgain"), initial: false },
        okText: t("common.ok"),
      });
      if (!answer.ok) return;
      const action = answer.selected === "minimize" || answer.selected === "quit" ? answer.selected : "tray";
      if (answer.checked) {
        setCloseAction(action);
        await host.run({ type: "settings.closeAction", action });
      }
      performClose(action);
    } finally {
      closing.current = false;
    }
  };

  useEffect(() => {
    bridge.on(async (m) => {
      if (m.type === "state") {
        setLocale(m.state.locale);
        setState(m.state);
      }
      if (m.type === "dialog") {
        const answer = await host.ask({
          title: m.kind === "confirm" ? t("app.confirmTitle") : t("app.noticeTitle"),
          message: m.message,
          cancelText: m.kind === "confirm" ? t("common.cancel") : null,
        });
        bridge.notify({ type: "dialog.answer", dialogId: m.id, ok: answer.ok });
      }
    });
    bridge.notify({ type: "ready" });
  }, []);

  useEffect(() => {
    if (state) applyTheme(state.settings.theme);
  }, [state?.settings.theme]);

  useEffect(() => {
    if (state?.settings.closeAction) setCloseAction(state.settings.closeAction);
  }, [state?.settings.closeAction]);

  useEffect(() => {
    const news = state?.whatsNew;
    if (!news || shownWhatsNew.current) return;
    shownWhatsNew.current = true;
    void host
      .ask({
        title: news.title,
        message: news.summary,
        sections: news.sections.map((s) => ({ title: s.version, items: s.items })),
        okText: t("common.gotIt"),
        cancelText: null,
      })
      .then(() => host.run({ type: "changelog.seen" }));
  }, [state]);

  useEffect(() => {
    if (state && state.settings.shareUsage === null && !askedSharing.current) {
      askedSharing.current = true;
      void askSharing(host);
    }
  }, [state]);

  return (
    <HostContext.Provider value={host}>
      <div class="window">
        <div class="chrome" inert={dialogOpen}>
          <TitleBar bridge={bridge} onClose={() => void closePanel()}>
            {state && <RoomSwitcher state={state} onManage={() => setTab("room")} />}
          </TitleBar>
        </div>
        <div class="body" inert={dialogOpen}>
          <nav class="rail" aria-label={t("app.pages")}>
            {tabs.map((item) => (
              <button
                key={item.id}
                class="rail-item"
                aria-current={tab === item.id ? "page" : undefined}
                title={t(item.name)}
                onClick={() => {
                  if (item.id === "stats") setStatsTarget(null);
                  setTab(item.id);
                }}
              >
                <Icon name={item.icon} />
                <span>{t(item.name)}</span>
              </button>
            ))}
            {state && (
              <span class={state.connection.connected ? "rail-conn ok" : "rail-conn"} title={state.connection.text} role="status" aria-label={state.connection.text} />
            )}
          </nav>
          <main class="content">
            {!state ? (
              <p class="loading">{t("common.loading")}</p>
            ) : (
              <>
                {!state.connection.connected && (
                  <p class="infobar warning" role="status">
                    <Icon name="warning" />
                    {state.connection.text}
                  </p>
                )}
                <div class="page" key={tab}>
                  {tab === "status" && (
                    <StatusPage
                      state={state}
                      onViewStats={(m) => {
                        setStatsTarget(m);
                        setTab("stats");
                      }}
                    />
                  )}
                  {tab === "stats" && <StatsPage state={state} target={statsTarget} onTarget={setStatsTarget} />}
                  {tab === "room" && <RoomPage state={state} />}
                  {tab === "rules" && <RulesPage state={state} />}
                  {tab === "settings" && <SettingsPage state={state} />}
                </div>
              </>
            )}
          </main>
        </div>
      </div>
      {overlay}
    </HostContext.Provider>
  );
}

function RoomSwitcher({ state, onManage }: { state: PanelState; onManage: () => void }) {
  const { run } = useHost();
  const current = state.rooms.find((r) => r.id === state.selectedRoomId);
  return (
    <Flyout
      label={t("app.switchRoom")}
      header={t("app.switchRoom")}
      button={
        <>
          <RoomBadge id={current?.id} name={current?.name} size="small" />
          <span class="flyout-label">{current?.name ?? t("app.noRoom")}</span>
          {current?.onlineCount != null && (
            <span class="flyout-meta">
              <span class="online-dot" />
              {t("app.onlineCount", { count: current.onlineCount })}
            </span>
          )}
        </>
      }
    >
      {(close) => (
        <>
          {state.rooms.map((r) => (
            <button
              key={r.id}
              role="menuitemradio"
              aria-checked={r.id === state.selectedRoomId}
              class="menu-item"
              onClick={() => {
                close();
                if (r.id !== state.selectedRoomId) void run({ type: "room.select", roomId: r.id });
              }}
            >
              <RoomBadge id={r.id} name={r.name} />
              <span class="menu-text">{r.name}</span>
              <span class="menu-meta">
                <Icon name="people" />
                {r.memberCount}/{r.maxMembers}
              </span>
            </button>
          ))}
          <button
            role="menuitemradio"
            aria-checked={!state.selectedRoomId}
            class="menu-item"
            onClick={() => {
              close();
              if (state.selectedRoomId) void run({ type: "room.select", roomId: null });
            }}
          >
            <RoomBadge />
            <span class="menu-text">{t("app.noRoom")}</span>
          </button>
          <hr class="menu-sep" />
          <button
            role="menuitem"
            class="menu-item"
            onClick={() => {
              close();
              onManage();
            }}
          >
            <span class="menu-icon">
              <Icon name="settings" />
            </span>
            <span class="menu-text">{t("app.manageRooms")}</span>
          </button>
        </>
      )}
    </Flyout>
  );
}
