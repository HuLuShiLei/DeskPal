// 宿主（C#）和页面之间的消息。字段和 C# 的 PanelState、PanelBridge、LoginWindow 一一对应，两边一起改。

export type Tone = "work" | "chat" | "relax" | "game" | "browse" | "other" | "away" | "offline";

export interface PresenceView {
  headline: string;
  app: string | null;
  detail: string | null;
  tone: Tone;
}

/** AppTitle：桌面应用的窗口标题包含关键词时，pattern 是「进程名/关键词」。 */
export type RuleKind = "Process" | "Domain" | "TitleKeyword" | "AppTitle";

export interface MyRuleItem {
  kind: RuleKind;
  pattern: string;
  name: string;
  kindName: string;
  category: string;
  categoryName: string;
  /** 名称下面显示的匹配内容：AppTitle 是关键词，其他是 pattern。 */
  shown: string;
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
  /** 当前房间的在线人数（含自己）；其他房间和未连接时为 null。 */
  onlineCount: number | null;
  /** 我加入这个房间的日期，例如「11 月 12 日加入」。 */
  joined: string | null;
}

/** 登录的客户端。 */
export type ClientKind = "windows" | "mac" | "linux";

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
  /** 对方登录的客户端；离线、已屏蔽或旧版客户端时为 null。 */
  client: ClientKind | null;
  /** 加入这个房间的日期，例如「11 月 12 日加入」。 */
  joined: string | null;
  /** 和离线、已屏蔽的成员一起排在后面。刚下线的成员先留在原位，1 分钟后才移下去。 */
  trailing: boolean;
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

/** 插件能提供的几类信息。和 C# 的 ProviderCapability 一致。 */
export type PluginCapability = "foreground" | "domain" | "idle" | "lock" | "media" | "detail";

export interface PluginNote {
  /** 翻译键，前面加 plugins.note. 查文字；找不到时显示 text。 */
  key: string;
  text: string | null;
  tone: "info" | "warn";
}

export interface PluginView {
  /** official:windows、official:macos、official:linux，或 user:文件名。 */
  id: string;
  name: string;
  version: string | null;
  /** DeskPal 自带的插件（相当于 mod 列表里的本体和 DLC）。 */
  official: boolean;
  enabled: boolean;
  provides: PluginCapability[];
  /** provides 里实际在用的部分；其余被列表上方的插件覆盖。 */
  owns: PluginCapability[];
  state: "disabled" | "starting" | "running" | "failed";
  notes: PluginNote[];
  /** 插件提供的按钮，按钮文字用 plugins.action.<id> 翻译。 */
  actions: string[];
  /** 补充标题（detail）适用的应用（进程名）。owns 里有 detail 时表示正在显示它补充的标题。 */
  apps: string[];
}

export interface PluginsView {
  items: PluginView[];
  /** 现在没有任何开启的插件提供的信息。 */
  unprovided: PluginCapability[];
  folder: string;
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
    userId: string;
    /** 这台电脑的客户端。 */
    client: ClientKind | null;
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
    /** 公开的统计包含暂停分享期间的实际活动；否则这段时间只显示为暂停分享。 */
    sharePausedStats: boolean;
  } | null;
  /** title：前台桌面应用的窗口标题，用作「只在标题包含」的默认文字，浏览器为 null。keyword：当前结果来自哪条 AppTitle 规则的关键词。 */
  correction: { target: string | null; current: string; options: Option<string>[]; personal: boolean; title: string | null; keyword: string | null };
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
    /** ask 每次询问，minimize 最小化，tray 最小化到系统托盘，quit 直接退出。 */
    closeAction: CloseAction;
    /** system 跟随 Windows 显示语言，zh-CN / en 固定。 */
    language: Language;
    /** 服务器把这个账号列为管理员，设置页显示注册邀请码。 */
    isAdmin: boolean;
    /** 无操作超过这么多分钟后显示暂离。 */
    idleMinutes: number;
    /** 新状态保持这么多秒不变后才同步给房间成员；0 立即同步。只影响推送，不影响使用统计。 */
    switchDelaySeconds: number;
    /** 个人数据（个人分类、不记录标题、偏好设置）除本机外还保存在哪里。同步到账号和同步文件夹只能开启一种。 */
    personal: { accountSync: boolean; folder: string | null; folderUnavailable: boolean } | null;
    /** 插件列表（像 mod 列表一样有先后）：当前窗口、网站、暂离、锁屏、媒体各取最上面一个开启的插件提供的。 */
    plugins?: PluginsView | null;
  };
  myRules: MyRuleItem[];
  /** 不记录标题：命中时房间、首页和使用统计都不显示标题。 */
  noTitleRules: NoTitleItem[] | null;
  unclassified: UnclassifiedItem[];
  /** 更新后还没看过的更新内容；关闭对话框后为 null。 */
  whatsNew: { title: string; summary: string; sections: { version: string; items: string[] }[] } | null;
  /** 我自己的使用统计设置。 */
  usage: {
    /** 最近 7 天查看过我的统计的人，最新的在前。 */
    viewers: { name: string; room: string; when: string }[];
    /** 同步到服务器，账号不在服务器名单里时为 null。needsPin：其他电脑开启的，这台电脑还要输入 PIN 码；error：上次同步失败、用户可以处理的原因。 */
    sync: { enabled: boolean; needsPin: boolean; error: string | null } | null;
    /** 同步文件夹。和同步到服务器只能开启一种。unavailable：已开启，但文件夹暂时无法访问。 */
    folder: { enabled: boolean; path: string | null; unavailable: boolean } | null;
  } | null;
  /** 文案实际使用的语言。宿主推送的文字已经是这个语言，页面自己的文字跟着切换。 */
  locale: "zh-CN" | "en";
}

export type Language = "system" | "zh-CN" | "en";

export type NoTitleKind = "App" | "Process" | "Domain" | "Title";
export interface NoTitleItem {
  kind: NoTitleKind;
  pattern: string;
  label: string;
}
export type StatsScale = "Day" | "Week" | "Month";
/** 排行列出什么。图表固定按类别着色。 */
export type StatsRankBy = "Category" | "App" | "Title";

/** 统计页的一页，由 stats.query 按需返回，不随 PanelState 推送。时间都是秒。 */
export interface StatsView {
  scale: StatsScale;
  rankBy: StatsRankBy;
  /** 只看的类别（图例的 key），总时长、图表和排行都只算这一类；null 表示全部。 */
  filter: string | null;
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
  bars: StatsBar[];
  barMax: number;
  barMaxLabel: string;
  /** 只有日视图有：从零点起的秒数。 */
  timeline: { start: number; end: number; tone: Tone; tip: string }[];
  ranking: StatsRankItem[];
  note: string | null;
  empty: string | null;
  /** 和上一周期相比变化最大的类别，显示在对比下面。 */
  changes: { key: string; name: string; tone: Tone; text: string; hidden: boolean }[];
  /** 首次和最后活动；周、月是平均值。只在看自己时有。 */
  bounds: string | null;
  /** 一周节律：周一到周日，每行 24 格。只在看自己的周、月时有。 */
  rhythm: StatsRhythm | null;
}

export interface StatsRhythm {
  rows: { label: string; cells: { seconds: number; tip: string }[] }[];
  /** 只看某一类时用这一类的颜色；null 用强调色。 */
  tone: Tone | null;
  max: number;
}

export interface StatsBar {
  label: string;
  tip: string;
  date: string | null;
  parts: { key: string; seconds: number }[];
}

export interface StatsRankItem {
  name: string;
  sub: string | null;
  tone: Tone;
  time: string;
  share: number;
  hidden: boolean;
  /** 只在自己的应用、标题排行上有：点击后用 stats.app 打开详情。 */
  detail: StatsDetailKey | null;
  /** 只在类别排行上有：点击后只看这一类。 */
  filter: string | null;
}

export interface StatsDetailKey {
  app: string;
  title: string | null;
}

/** 某个应用、网站或标题的详情，由 stats.app 返回，范围和统计页相同。 */
export interface StatsAppView {
  scale: StatsScale;
  anchor: string;
  name: string;
  sub: string | null;
  title: string;
  canPrev: boolean;
  canNext: boolean;
  totalLabel: string;
  total: string;
  summary: string | null;
  facts: { label: string; value: string }[];
  legend: StatsView["legend"];
  /** 日视图 24 根（每小时），周、月视图每天一根。 */
  bars: StatsBar[];
  barMax: number;
  barMaxLabel: string;
  /** 周、月视图按小时的分布；日视图为空。 */
  hours: StatsBar[];
  hoursMax: number;
  hoursMaxLabel: string;
  /** 常见标题；查看某个标题时为空。 */
  titles: StatsRankItem[];
  /** 修改分类时用 rules.set 保存的个人分类；null 表示不能修改。 */
  rule: { kind: RuleKind; pattern: string; name: string; category: string } | null;
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
  | { type: "room.inviteGet"; roomId: string }
  | { type: "room.inviteCopy"; roomId: string }
  | { type: "room.invite"; roomId: string }
  | { type: "room.privacy"; roomId: string; levels: Record<string, PrivacyLevel> }
  | { type: "room.shareStats"; roomId: string; enabled: boolean; days: number }
  | { type: "room.sharePausedStats"; roomId: string; enabled: boolean }
  | { type: "stats.query"; scale: StatsScale; rankBy: StatsRankBy; filter: string | null; date: string | null; userId: string | null; hours: number[] | null }
  | { type: "stats.app"; scale: StatsScale; date: string | null; app: string; title: string | null }
  | { type: "stats.export" }
  | { type: "stats.import" }
  | { type: "stats.clear" }
  | { type: "stats.sync.enable"; pin: string }
  | { type: "stats.sync.unlock"; pin: string }
  | { type: "stats.sync.disable" }
  | { type: "stats.folder.choose" }
  | { type: "stats.folder.enable"; path: string; pin: string }
  | { type: "stats.folder.stop" }
  | { type: "noTitle.add"; kind: NoTitleKind; pattern: string }
  | { type: "noTitle.remove"; kind: NoTitleKind; pattern: string }
  | { type: "personal.sync.enable" }
  | { type: "personal.sync.disable" }
  | { type: "personal.folder.choose" }
  | { type: "personal.folder.stop" }
  | { type: "personal.export" }
  | { type: "personal.import" }
  | { type: "room.transfer"; roomId: string; userId: string }
  | { type: "room.kick"; roomId: string; userId: string }
  | { type: "room.leave"; roomId: string }
  | { type: "room.dissolve"; roomId: string }
  | { type: "member.pin"; userId: string }
  | { type: "member.unpin"; userId: string }
  | { type: "member.reorder"; roomId: string; userIds: string[] }
  | { type: "member.block"; roomId: string; userId: string }
  | { type: "member.unblock"; roomId: string; userId: string }
  | { type: "presence.pause"; paused: boolean }
  /** keyword：只对标题包含它的窗口生效；没有时改整个应用。 */
  | { type: "presence.correct"; category: string; keyword?: string | null }
  | { type: "presence.resetCorrection" }
  /** AppTitle 规则可以带 keyword，替换 pattern 里的关键词。 */
  | { type: "rules.set"; kind: RuleKind; pattern: string; name: string | null; category: string; keyword?: string | null }
  | { type: "rules.delete"; kind: RuleKind; pattern: string }
  | { type: "me.rename"; name: string }
  | { type: "settings.shareUsage"; enabled: boolean }
  | { type: "settings.autoStart"; enabled: boolean }
  | { type: "settings.topmost"; enabled: boolean }
  | { type: "settings.theme"; theme: "system" | "light" | "dark" }
  | { type: "settings.closeAction"; action: CloseAction }
  | { type: "settings.language"; language: Language }
  | { type: "settings.idleMinutes"; minutes: number }
  | { type: "settings.switchDelay"; seconds: number }
  | { type: "update.check" }
  | { type: "update.beta"; enabled: boolean }
  | { type: "update.restart" }
  | { type: "changelog.seen" }
  | { type: "logs.open" }
  | { type: "plugins.setEnabled"; id: string; enabled: boolean }
  | { type: "plugins.move"; id: string; offset: number }
  | { type: "plugins.action"; id: string; action: string }
  | { type: "plugins.refresh" }
  | { type: "plugins.openFolder" }
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

/** ask 每次询问，minimize 最小化，tray 最小化到系统托盘，quit 直接退出。 */
export type CloseAction = "ask" | "minimize" | "tray" | "quit";

/**
 * 不需要回复的消息。window.* 是自绘标题栏的按钮，由窗口直接处理。
 * window.drag、window.dblclick 只在 macOS 上发：WKWebView 不支持 CSS app-region，按下标题栏时由窗口跟随鼠标移动。
 */
export type Notice =
  | { type: "ready" }
  | { type: "dialog.answer"; dialogId: number; ok: boolean }
  | { type: "window.minimize" }
  | { type: "window.close" }
  | { type: "window.quit" }
  | { type: "window.drag"; x: number; y: number }
  | { type: "window.dblclick" };

export interface CommandResult {
  ok: boolean;
  error: string | null;
  data?: unknown;
}

/** 消息通道：Windows 的 WebView2 里是 chrome.webview，macOS 的 WKWebView 里是 webkit.messageHandlers，浏览器里是模拟宿主。 */
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
    webkit?: { messageHandlers?: { deskpal?: { postMessage(message: string): void } } };
    /** macOS 宿主调用它把消息交给页面。 */
    __deskpal?: (message: HostMessage) => void;
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

  const webkit = window.webkit?.messageHandlers?.deskpal;
  if (webkit) {
    return new Bridge(
      {
        post: (m) => webkit.postMessage(JSON.stringify(m)),
        listen: (l) => {
          window.__deskpal = l;
        },
      },
      false,
    );
  }

  const { createMockTransport } = await import("./mock/mockHost");
  return new Bridge(createMockTransport(new URLSearchParams(location.search)), true);
}
