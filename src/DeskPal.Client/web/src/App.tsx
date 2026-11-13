import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge, PanelState } from "./bridge";
import { HostContext, useHost, useHostServices } from "./host";
import { RoomPage } from "./pages/RoomPage";
import { RulesPage } from "./pages/RulesPage";
import { SettingsPage } from "./pages/SettingsPage";
import { StatsPage, type StatsTarget } from "./pages/StatsPage";
import { StatusPage } from "./pages/StatusPage";
import { setLocale, t, type MessageKey } from "./i18n";
import { askSharing } from "./sharing";
import { applyTheme } from "./theme";
import { Flyout, Icon, TitleBar, type IconName } from "./ui";

const tabs: { id: Tab; name: MessageKey; icon: IconName }[] = [
  { id: "status", name: "tab.status", icon: "people" },
  { id: "stats", name: "tab.stats", icon: "chart" },
  { id: "room", name: "tab.room", icon: "home" },
  { id: "rules", name: "tab.rules", icon: "tag" },
  { id: "settings", name: "tab.settings", icon: "settings" },
];

type Tab = "status" | "stats" | "room" | "rules" | "settings";

export function App({ bridge }: { bridge: Bridge }) {
  const [state, setState] = useState<PanelState | null>(null);
  const [tab, setTab] = useState<Tab>("status");
  const [statsTarget, setStatsTarget] = useState<StatsTarget>(null);
  const [closeAction, setCloseAction] = useState<"ask" | "minimize" | "tray">("ask");
  const { host, overlay, dialogOpen } = useHostServices(bridge);
  const askedSharing = useRef(false);
  const shownWhatsNew = useRef(false);
  const closing = useRef(false);

  const closePanel = async () => {
    if (closing.current) return;
    closing.current = true;
    try {
      let action: "minimize" | "tray" = closeAction === "minimize" ? "minimize" : "tray";
      if (closeAction !== "minimize" && closeAction !== "tray") {
        const answer = await host.ask({
          title: t("app.closeTitle"),
          message: t("app.closeMessage"),
          select: {
            presentation: "radio",
            initial: "tray",
            options: [
              { value: "minimize", name: t("common.minimize") },
              { value: "tray", name: t("settings.closeTray") },
            ],
          },
          checkbox: { label: t("app.dontAskAgain"), initial: false },
          okText: t("common.ok"),
        });
        if (!answer.ok) return;
        action = answer.selected === "minimize" ? "minimize" : "tray";
        if (answer.checked) {
          setCloseAction(action);
          await host.run({ type: "settings.closeAction", action });
        }
      }
      bridge.notify({ type: action === "minimize" ? "window.minimize" : "window.close" });
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
          <TitleBar bridge={bridge} title="DeskPal" onClose={() => void closePanel()}>
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
    <Flyout label={t("app.switchRoom")} button={<span class="flyout-label">{current?.name ?? t("app.noRoom")}</span>}>
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
              <span class="menu-check">{r.id === state.selectedRoomId && <Icon name="accept" />}</span>
              <span class="menu-text">{r.name}</span>
              <span class="menu-meta">
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
            <span class="menu-check">{!state.selectedRoomId && <Icon name="accept" />}</span>
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
            <span class="menu-check">
              <Icon name="settings" />
            </span>
            <span class="menu-text">{t("app.manageRooms")}</span>
          </button>
        </>
      )}
    </Flyout>
  );
}
