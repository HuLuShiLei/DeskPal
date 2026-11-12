import { useEffect, useRef, useState } from "preact/hooks";
import type { Bridge, PanelState } from "./bridge";
import { HostContext, useHost, useHostServices } from "./host";
import { RoomPage } from "./pages/RoomPage";
import { SettingsPage } from "./pages/SettingsPage";
import { StatusPage } from "./pages/StatusPage";
import { askSharing } from "./sharing";
import { applyTheme } from "./theme";
import { Flyout, Icon, TitleBar, type IconName } from "./ui";

const tabs: { id: Tab; name: string; icon: IconName }[] = [
  { id: "status", name: "状态", icon: "people" },
  { id: "room", name: "房间", icon: "home" },
  { id: "settings", name: "设置", icon: "settings" },
];

type Tab = "status" | "room" | "settings";

export function App({ bridge }: { bridge: Bridge }) {
  const [state, setState] = useState<PanelState | null>(null);
  const [tab, setTab] = useState<Tab>("status");
  const { host, overlay, dialogOpen } = useHostServices(bridge);
  const askedSharing = useRef(false);

  useEffect(() => {
    bridge.on(async (m) => {
      if (m.type === "state") setState(m.state);
      if (m.type === "dialog") {
        const answer = await host.ask({
          title: m.kind === "confirm" ? "请确认" : "提示",
          message: m.message,
          cancelText: m.kind === "confirm" ? "取消" : null,
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
    if (state && state.settings.shareUsage === null && !askedSharing.current) {
      askedSharing.current = true;
      void askSharing(host);
    }
  }, [state]);

  return (
    <HostContext.Provider value={host}>
      <div class="window">
        <div class="chrome" inert={dialogOpen}>
          <TitleBar bridge={bridge} title="DeskPal">
            {state && <RoomSwitcher state={state} onManage={() => setTab("room")} />}
          </TitleBar>
        </div>
        <div class="body" inert={dialogOpen}>
          <nav class="rail" aria-label="页面">
            {tabs.map((t) => (
              <button key={t.id} class="rail-item" aria-current={tab === t.id ? "page" : undefined} title={t.name} onClick={() => setTab(t.id)}>
                <Icon name={t.icon} />
                <span>{t.name}</span>
              </button>
            ))}
            {state && (
              <span class={state.connection.connected ? "rail-conn ok" : "rail-conn"} title={state.connection.text} role="status" aria-label={state.connection.text} />
            )}
          </nav>
          <main class="content">
            {!state ? (
              <p class="loading">正在加载…</p>
            ) : (
              <>
                {!state.connection.connected && (
                  <p class="infobar warning" role="status">
                    <Icon name="warning" />
                    {state.connection.text}
                  </p>
                )}
                <div class="page" key={tab}>
                  {tab === "status" && <StatusPage state={state} />}
                  {tab === "room" && <RoomPage state={state} />}
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
    <Flyout label="切换房间" button={<span class="flyout-label">{current?.name ?? "没有房间"}</span>}>
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
          {state.rooms.length > 0 && <hr class="menu-sep" />}
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
            <span class="menu-text">管理房间</span>
          </button>
        </>
      )}
    </Flyout>
  );
}
