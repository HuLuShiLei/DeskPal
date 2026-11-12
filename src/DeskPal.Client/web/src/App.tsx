import { useEffect, useState } from "preact/hooks";
import type { Bridge, PanelState } from "./bridge";
import { HostContext, useHostServices } from "./host";
import { RoomPage } from "./pages/RoomPage";
import { SettingsPage } from "./pages/SettingsPage";
import { StatusPage } from "./pages/StatusPage";

const tabs = [
  { id: "status", name: "状态" },
  { id: "room", name: "房间" },
  { id: "settings", name: "设置" },
] as const;

type Tab = (typeof tabs)[number]["id"];

export function App({ bridge }: { bridge: Bridge }) {
  const [state, setState] = useState<PanelState | null>(null);
  const [tab, setTab] = useState<Tab>("status");
  const { host, overlay, dialogOpen } = useHostServices(bridge);

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

  if (!state) return <p class="loading">正在加载…</p>;

  return (
    <HostContext.Provider value={host}>
      <header class="topbar" inert={dialogOpen}>
        {state.rooms.length > 0 ? (
          <select
            aria-label="当前房间"
            value={state.selectedRoomId ?? ""}
            onChange={(e) => host.run({ type: "room.select", roomId: e.currentTarget.value })}
          >
            {state.rooms.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </select>
        ) : (
          <span class="muted">没有房间</span>
        )}
        <span class={state.connection.connected ? "conn ok" : "conn"}>{state.connection.text}</span>
      </header>
      <nav class="tabs" role="tablist" inert={dialogOpen}>
        {tabs.map((t) => (
          <button key={t.id} role="tab" aria-selected={tab === t.id} onClick={() => setTab(t.id)}>
            {t.name}
          </button>
        ))}
      </nav>
      <main inert={dialogOpen}>
        {tab === "status" && <StatusPage state={state} />}
        {tab === "room" && <RoomPage state={state} />}
        {tab === "settings" && <SettingsPage state={state} />}
      </main>
      {overlay}
    </HostContext.Provider>
  );
}
