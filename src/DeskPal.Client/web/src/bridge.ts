// 宿主（C#）和页面之间的消息。字段和 C# 的 PanelState、PanelBridge、LoginWindow 一一对应，两边一起改。

export type Tone = "work" | "chat" | "relax" | "game" | "browse" | "other" | "away" | "offline";

export interface PresenceView {
  headline: string;
  app: string | null;
  detail: string | null;
  tone: Tone;
}

export type RuleKind = "Process" | "Domain" | "TitleKeyword";

export interface MyRuleItem {
  kind: RuleKind;
  pattern: string;
  name: string;
  kindName: string;
  category: string;
  categoryName: string;
}

export interface UnclassifiedItem {
  kind: RuleKind;
  pattern: string;
  name: string;
  today: string;
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
  /** 已置顶为桌面小卡片。 */
  pinned: boolean;
  /** 我屏蔽了该成员：双方互相看不到状态，对方不知道。 */
  blocked: boolean;
  /** 该成员在这个房间公开了使用统计，显示「查看统计」。 */
  shareStats: boolean;
}

/** 置顶小卡片的内容。detail 是应用名和详情合成的一行。 */
export interface CardMessage {
  type: "card";
  member: MemberItem;
  detail: string | null;
  theme: "system" | "light" | "dark";
  locale: "zh-CN" | "en";
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
    /** 当前应用是怎么识别的，悬停显示。 */
    source: string | null;
    visible: string | null;
    paused: boolean;
    /** 本机的新状态还没稳定、尚未同步给房间成员时才有。 */
    pending: { view: PresenceView; remainingMs: number; totalSeconds: number; full: string | null } | null;
    meta: string | null;
    /** 不经隐私设置过滤的类别、应用和标题，只在悬停时给自己看。 */
    full: string | null;
  };
  rooms: RoomItem[];
  selectedRoomId: string | null;
  emptyHint: string | null;
  members: MemberItem[];
  privacy: {
    rows: PrivacyRow[];
    levels: Option<PrivacyLevel>[];
    /** 房间成员可以查看我的使用统计，细粒度按上面每个类别的级别。 */
    shareStats: boolean;
    statsDays: number;
    dayOptions: { value: number; name: string }[];
  } | null;
  correction: { target: string | null; current: string; options: Option<string>[]; personal: boolean };
  settings: {
    autoStart: boolean;
    topmost: boolean;
    receiveBeta: boolean;
    updateReady: boolean;
    updateActivity: string | null;
    version: string;
    userName: string;
    /** null：还没有回答是否分享使用数据。 */
    shareUsage: boolean | null;
    /** system 跟随 Windows，light / dark 固定。 */
    theme: "system" | "light" | "dark";
    /** ask 每次询问，minimize 最小化，tray 最小化到系统托盘。 */
    closeAction: "ask" | "minimize" | "tray";
    /** system 跟随 Windows 显示语言，zh-CN / en 固定。 */
    language: Language;
    /** 服务器把这个账号列为管理员，设置页显示注册邀请码。 */
    isAdmin: boolean;
  };
  myRules: MyRuleItem[];
  unclassified: UnclassifiedItem[];
  /** 更新后还没看过的更新内容；关闭对话框后为 null。 */
  whatsNew: { title: string; summary: string; sections: { version: string; items: string[] }[] } | null;
  /** 我自己的使用统计设置。 */
  usage: {
    retentionDays: number;
    retentionOptions: { value: number; name: string }[];
    /** 不记录标题的应用和网站。 */
    noTitleRules: { kind: NoTitleKind; pattern: string; label: string }[];
    /** 最近 7 天查看过我的统计的人，最新的在前。 */
    viewers: { name: string; room: string; when: string }[];
  } | null;
  /** 文案实际使用的语言。宿主推送的文字已经是这个语言，页面自己的文字跟着切换。 */
  locale: "zh-CN" | "en";
}

export type Language = "system" | "zh-CN" | "en";

export type NoTitleKind = "App" | "Process" | "Domain" | "Title";
export type StatsScale = "Day" | "Week" | "Month";
export type StatsGranularity = "State" | "Category" | "Title";

/** 统计页的一页，由 stats.query 按需返回，不随 PanelState 推送。时间都是秒。 */
export interface StatsView {
  scale: StatsScale;
  granularity: StatsGranularity;
  /** yyyy-MM-dd，范围里的任意一天。 */
  anchor: string;
  /** 查看别人时是对方的名字；看自己时为 null。 */
  owner: string | null;
  title: string;
  totalLabel: string;
  total: string;
  compare: string | null;
  summary: string | null;
  canPrev: boolean;
  canNext: boolean;
  legend: { key: string; name: string; tone: Tone; hidden: boolean }[];
  /** 日视图 24 根（每小时），周、月视图每天一根。 */
  bars: { label: string; tip: string; date: string | null; parts: { key: string; seconds: number }[] }[];
  barMax: number;
  barMaxLabel: string;
  /** 只有日视图有：从零点起的秒数。 */
  timeline: { start: number; end: number; tone: Tone; tip: string }[];
  ranking: { name: string; sub: string | null; tone: Tone; time: string; share: number; hidden: boolean }[];
  rankingTitle: string;
  note: string | null;
  empty: string | null;
}

export type DialogKind = "confirm" | "info" | "warning";

export type HostMessage =
  | { type: "state"; state: PanelState }
  | { type: "result"; id: number; ok: boolean; error: string | null; data?: unknown }
  | { type: "dialog"; id: number; kind: DialogKind; message: string }
  | { type: "login"; userName: string; notice?: string | null }
  | CardMessage;

export type Command =
  | { type: "room.select"; roomId: string | null }
  | { type: "room.create"; name: string }
  | { type: "room.join"; code: string }
  | { type: "room.rename"; roomId: string; name: string }
  | { type: "room.invite"; roomId: string }
  | { type: "room.privacy"; roomId: string; levels: Record<string, PrivacyLevel> }
  | { type: "room.shareStats"; roomId: string; enabled: boolean; days: number }
  | { type: "stats.query"; scale: StatsScale; granularity: StatsGranularity; date: string | null; userId: string | null }
  | { type: "stats.export" }
  | { type: "stats.import" }
  | { type: "stats.clear" }
  | { type: "stats.retention"; days: number }
  | { type: "stats.noTitle.add"; kind: NoTitleKind; pattern: string }
  | { type: "stats.noTitle.remove"; kind: NoTitleKind; pattern: string }
  | { type: "room.transfer"; roomId: string; userId: string }
  | { type: "room.kick"; roomId: string; userId: string }
  | { type: "room.leave"; roomId: string }
  | { type: "room.dissolve"; roomId: string }
  | { type: "member.pin"; userId: string }
  | { type: "member.unpin"; userId: string }
  | { type: "member.block"; roomId: string; userId: string }
  | { type: "member.unblock"; roomId: string; userId: string }
  | { type: "presence.pause"; paused: boolean }
  | { type: "presence.correct"; category: string }
  | { type: "presence.resetCorrection" }
  | { type: "rules.set"; kind: RuleKind; pattern: string; name: string | null; category: string }
  | { type: "rules.delete"; kind: RuleKind; pattern: string }
  | { type: "me.rename"; name: string }
  | { type: "settings.shareUsage"; enabled: boolean }
  | { type: "settings.autoStart"; enabled: boolean }
  | { type: "settings.topmost"; enabled: boolean }
  | { type: "settings.theme"; theme: "system" | "light" | "dark" }
  | { type: "settings.closeAction"; action: "ask" | "minimize" | "tray" }
  | { type: "settings.language"; language: Language }
  | { type: "update.check" }
  | { type: "update.beta"; enabled: boolean }
  | { type: "update.restart" }
  | { type: "changelog.seen" }
  | { type: "auth.login"; userName: string; password: string }
  | { type: "signupInvites.list" }
  | { type: "signupInvites.create" }
  | { type: "signupInvites.copy"; code: string }
  | { type: "signupInvites.revoke"; code: string }
  | { type: "auth.register"; userName: string; password: string; displayName: string; signupCode: string }
  | { type: "auth.logout" };

/** 注册邀请码列表的一行。text 是宿主按语言生成的「生成时间」或「谁在什么时候用它注册」。 */
export interface SignupInviteItem {
  code: string;
  used: boolean;
  text: string;
}

/** 不需要回复的消息。window.* 是自绘标题栏的按钮，由窗口直接处理。 */
export type Notice =
  | { type: "ready" }
  | { type: "dialog.answer"; dialogId: number; ok: boolean }
  | { type: "window.minimize" }
  | { type: "window.close" };

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
