// 宿主（C#）和页面之间的消息。字段和 C# 的 PanelState、PanelBridge、LoginWindow 一一对应，两边一起改。

export interface PresenceView {
  headline: string;
  app: string | null;
  detail: string | null;
}

export interface RoomItem {
  id: string;
  name: string;
  isOwner: boolean;
  memberCount: number;
  maxMembers: number;
}

export interface MemberItem {
  userId: string;
  name: string;
  isOwner: boolean;
  online: boolean;
  view: PresenceView;
  meta: string;
}

export interface Option<T extends string> {
  value: T;
  name: string;
}

export type PrivacyLevel = "StateOnly" | "Category" | "Detail";

export interface PrivacyRow {
  category: string;
  name: string;
  level: PrivacyLevel;
}

export interface PanelState {
  connection: { connected: boolean; text: string };
  me: {
    displayName: string;
    view: PresenceView;
    source: string | null;
    visible: string | null;
    paused: boolean;
    manualText: string | null;
    manualPresets: string[];
  };
  rooms: RoomItem[];
  selectedRoomId: string | null;
  emptyHint: string | null;
  members: MemberItem[];
  privacy: { rows: PrivacyRow[]; levels: Option<PrivacyLevel>[] } | null;
  correction: { target: string | null; current: string; options: Option<string>[] };
  settings: {
    autoStart: boolean;
    topmost: boolean;
    receiveBeta: boolean;
    updateReady: boolean;
    version: string;
    serverUrl: string;
    userName: string;
  };
}

export type DialogKind = "confirm" | "info" | "warning";

export type HostMessage =
  | { type: "state"; state: PanelState }
  | { type: "result"; id: number; ok: boolean; error: string | null; data?: unknown }
  | { type: "dialog"; id: number; kind: DialogKind; message: string }
  | { type: "login"; serverUrl: string; userName: string };

export type Command =
  | { type: "room.select"; roomId: string }
  | { type: "room.create"; name: string }
  | { type: "room.join"; code: string }
  | { type: "room.rename"; roomId: string; name: string }
  | { type: "room.invite"; roomId: string }
  | { type: "room.privacy"; roomId: string; levels: Record<string, PrivacyLevel> }
  | { type: "room.transfer"; roomId: string; userId: string }
  | { type: "room.leave"; roomId: string }
  | { type: "room.dissolve"; roomId: string }
  | { type: "member.poke"; roomId: string; userId: string; text: string | null }
  | { type: "presence.pause"; paused: boolean }
  | { type: "presence.manual"; text: string | null }
  | { type: "presence.correct"; category: string; shareToRoom: boolean }
  | { type: "settings.autoStart"; enabled: boolean }
  | { type: "settings.topmost"; enabled: boolean }
  | { type: "update.check" }
  | { type: "update.beta"; enabled: boolean }
  | { type: "update.restart" }
  | { type: "auth.login"; serverUrl: string; userName: string; password: string }
  | { type: "auth.register"; serverUrl: string; userName: string; password: string; displayName: string };

/** 不需要回复的消息。 */
export type Notice = { type: "ready" } | { type: "dialog.answer"; dialogId: number; ok: boolean };

export interface CommandResult {
  ok: boolean;
  error: string | null;
  data?: unknown;
}

/** 消息通道：WebView2 里是 chrome.webview，浏览器里是模拟宿主。 */
export interface Transport {
  post(message: unknown): void;
  listen(listener: (message: HostMessage) => void): void;
}

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (e: { data: HostMessage }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

export class Bridge {
  private nextId = 0;
  private readonly pending = new Map<number, (result: CommandResult) => void>();
  private readonly listeners: ((message: HostMessage) => void)[] = [];

  constructor(
    private readonly transport: Transport,
    readonly mock: boolean,
  ) {
    transport.listen((message) => {
      if (message.type === "result") {
        this.pending.get(message.id)?.({ ok: message.ok, error: message.error, data: message.data });
        this.pending.delete(message.id);
      }
      this.listeners.forEach((l) => l(message));
    });
  }

  request(command: Command): Promise<CommandResult> {
    const id = ++this.nextId;
    return new Promise((resolve) => {
      this.pending.set(id, resolve);
      this.transport.post({ id, ...command });
    });
  }

  notify(notice: Notice) {
    this.transport.post(notice);
  }

  on(listener: (message: HostMessage) => void) {
    this.listeners.push(listener);
  }
}

export async function createBridge(): Promise<Bridge> {
  const webview = window.chrome?.webview;
  if (webview) {
    return new Bridge(
      {
        post: (m) => webview.postMessage(m),
        listen: (l) => webview.addEventListener("message", (e) => l(e.data)),
      },
      false,
    );
  }

  const { createMockTransport } = await import("./mock/mockHost");
  return new Bridge(createMockTransport(new URLSearchParams(location.search)), true);
}