// 浏览器里开发用的模拟宿主。初始状态来自 C# 测试生成的 fixtures，命令的效果在这里粗略模拟。
// 地址参数：?mock=default|no-room|guest|updated 选样例；?lang=en 用英文样例；?fail 让所有命令失败；?page=login 打开登录页；?page=card 打开第一个成员的置顶小卡片；?sync=on|pin|error|folder|unavailable 让统计页的同步处于已开启、待输入 PIN 码、上传失败、同步文件夹已开启、文件夹无法访问；?existing 让选择的文件夹里已有其他电脑的同步数据；?crowd 多加几位各种状态的成员。
// 发出的命令记在 window.__sent，方便检查。

import type { CommandResult, HostMessage, MemberItem, NoTitleKind, PanelState, PrivacyLevel, SignupInviteItem, StatsAppView, StatsView, Transport } from "../bridge";
import defaultState from "./fixtures/default.json";
import statsViews from "./fixtures/stats.json";
import statsApps from "./fixtures/stats-apps.json";
import enStatsApps from "./fixtures/en/stats-apps.json";
import guestState from "./fixtures/guest.json";
import noRoomState from "./fixtures/no-room.json";
import updatedState from "./fixtures/updated.json";
import enDefaultState from "./fixtures/en/default.json";
import enStatsViews from "./fixtures/en/stats.json";
import enGuestState from "./fixtures/en/guest.json";
import enNoRoomState from "./fixtures/en/no-room.json";
import enUpdatedState from "./fixtures/en/updated.json";

declare global {
  interface Window {
    __sent?: unknown[];
  }
}

const fixtures: Record<string, PanelState> = {
  default: defaultState as PanelState,
  "no-room": noRoomState as PanelState,
  guest: guestState as PanelState,
  updated: updatedState as PanelState,
};

const enFixtures: Record<string, PanelState> = {
  default: enDefaultState as PanelState,
  "no-room": enNoRoomState as PanelState,
  guest: enGuestState as PanelState,
  updated: enUpdatedState as PanelState,
};

type Incoming = { id?: number; type: string; [key: string]: unknown };

/** ?crowd：再加几位状态各不相同的成员，检查卡片高度是否一致。 */
function crowd(L: (zh: string, en: string) => string): MemberItem[] {
  const member = (
    n: number,
    name: string,
    headline: string,
    tone: MemberItem["view"]["tone"],
    app: string | null,
    detail: string | null,
    meta: string,
    client: MemberItem["client"],
    extra: Partial<MemberItem> = {},
  ): MemberItem => ({
    userId: `00000000-0000-0000-0000-0000000c${String(n).padStart(4, "0")}`,
    name,
    isOwner: false,
    online: tone !== "offline",
    trailing: tone === "offline",
    view: { headline, app, detail, tone },
    meta,
    pinned: false,
    blocked: false,
    shareStats: false,
    client,
    joined: L(`11 月 ${n} 日加入`, `Joined Nov ${n}`),
    ...extra,
  });
  return [
    member(1, "summer", L("离线", "Offline"), "offline", null, null, "", null),
    member(2, "St", L("在看视频", "Watching videos"), "relax", L("抖音", "Douyin"), null, L("已持续 5 分钟", "For 5 min"), "windows", { shareStats: true }),
    member(3, "xxx", L("已锁屏", "Locked"), "away", null, null, L("已持续 1 小时 1 分钟", "For 1 hr 1 min"), "mac", { shareStats: true }),
    member(4, "邪能海胆", L("在聊天", "Chatting"), "chat", null, null, L("已持续 2 分钟", "For 2 min"), "windows"),
    member(5, "Ares Wang", L("正在浏览网页", "Browsing the web"), "browse", "help.github.com", "GitHub Help: GitHub Documentation, guides and API reference for everything", L("刚刚开始", "Just started"), "mac", { shareStats: true }),
    member(6, "池鱼", L("在聊天", "Chatting"), "chat", "QQ", null, L("刚刚开始", "Just started"), "windows"),
    member(7, "Mallory", L("已屏蔽", "Blocked"), "offline", null, null, "", null, { blocked: true, online: false }),
  ];
}

export function createMockTransport(params: URLSearchParams): Transport {
  const listeners: ((m: HostMessage) => void)[] = [];
  const english = params.get("lang") === "en";
  // 宿主生成的文字按语言给，和 C# 一致。
  const L = (zh: string, en: string) => (english ? en : zh);
  const samples = english ? enFixtures : fixtures;
  const views = (english ? enStatsViews : statsViews) as unknown as Record<string, StatsView>;
  const appViews = (english ? enStatsApps : statsApps) as unknown as Record<string, StatsAppView>;
  const initial = samples[params.get("mock") ?? "default"] ?? samples.default;
  const state: PanelState = structuredClone(initial);
  const syncSample = params.get("sync");
  const folderPath = L("D:\\OneDrive\\文档\\DeskPal 同步", "D:\\OneDrive\\Documents\\DeskPal sync");
  if (state.usage && (syncSample === "folder" || syncSample === "unavailable")) {
    state.usage.folder = { enabled: true, path: folderPath, unavailable: syncSample === "unavailable" };
  } else if (state.usage && syncSample) {
    const error = syncSample === "error" ? L("同步失败，将自动重试。", "Sync failed and will try again.") : null;
    state.usage.sync = { enabled: true, needsPin: syncSample === "pin", error };
  }
  if (params.has("crowd") && state.selectedRoomId) state.members.push(...crowd(L));
  // 和 C# 的 MemberOrder 一致：在线的在前，各组内按拖动的顺序。
  let memberOrder = state.members.map((x) => x.userId);
  const sortMembers = () =>
    state.members.sort((a, b) => Number(a.trailing) - Number(b.trailing) || memberOrder.indexOf(a.userId) - memberOrder.indexOf(b.userId));
  sortMembers();
  const personalSample = params.get("personal");
  if (personalSample === "account") state.settings.personal = { accountSync: true, folder: null, folderUnavailable: false };
  else if (personalSample === "folder" || personalSample === "unavailable")
    state.settings.personal = { accountSync: false, folder: folderPath, folderUnavailable: personalSample === "unavailable" };
  const fail = params.has("fail");
  const login = params.get("page") === "login";
  const card = params.get("page") === "card";
  let dialogId = 0;
  const dialogs = new Map<number, (ok: boolean) => void>();
  window.__sent = [];

  const emit = (m: HostMessage) => setTimeout(() => listeners.forEach((l) => l(m)), 30);
  const pushState = () => emit({ type: "state", state: structuredClone(state) });
  const ask = (kind: "confirm" | "info" | "warning", message: string) =>
    new Promise<boolean>((resolve) => {
      const id = ++dialogId;
      dialogs.set(id, resolve);
      emit({ type: "dialog", id, kind, message });
    });

  const room = (id: unknown) => state.rooms.find((r) => r.id === id);
  let signupInvites: SignupInviteItem[] = [
    { code: "P4W8N2KD", used: false, text: L("11 月 13 日 17:40 生成", "Created Nov 13, 17:40") },
    { code: "H3T9R6VB", used: true, text: L("Bob（bob）于 11 月 12 日 09:15 注册", "Used by Bob (bob) on Nov 12, 09:15") },
  ];
  const newCode = () => Array.from({ length: 8 }, () => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[Math.floor(Math.random() * 32)]).join("");
  const roomInvites = new Map<string, string>(state.selectedRoomId ? [[state.selectedRoomId, "K7Q2M9XA"]] : []);
  const inviteOf = (roomId: string) => roomInvites.get(roomId) ?? roomInvites.set(roomId, newCode()).get(roomId)!;

  async function handle(m: Incoming): Promise<CommandResult> {
    if (fail) return { ok: false, error: L("服务器暂时不可用，请稍后再试", "The server is temporarily unavailable. Try again later.") };
    switch (m.type) {
      case "auth.login":
        return m.password === "wrong"
          ? { ok: false, error: L("用户名或密码错误", "Incorrect user name or password") }
          : { ok: true, error: null };
      case "auth.register":
        return m.signupCode === "00000000"
          ? { ok: false, error: L("注册邀请码无效或已被使用", "This signup invite code is invalid or has already been used") }
          : { ok: true, error: null };
      case "signupInvites.list":
        return { ok: true, error: null, data: signupInvites };
      case "signupInvites.create": {
        const code = newCode();
        signupInvites = [{ code, used: false, text: L("刚刚生成", "Created just now") }, ...signupInvites];
        return {
          ok: true,
          error: null,
          data: {
            code,
            message: L(
              "注册邀请码已复制。一个注册邀请码只能注册一个账号，不会过期，没用之前可以在设置里作废。",
              "Signup invite code copied. Each code registers one account and does not expire. You can revoke it in Settings until it is used.",
            ),
            items: signupInvites,
          },
        };
      }
      case "signupInvites.copy":
        void navigator.clipboard.writeText(String(m.code)).catch(() => {});
        return { ok: true, error: null };
      case "signupInvites.revoke":
        signupInvites = signupInvites.filter((i) => i.code !== m.code);
        return { ok: true, error: null, data: signupInvites };
      case "room.select":
        state.selectedRoomId = (m.roomId as string | null) ?? null;
        Object.assign(
          state,
          state.selectedRoomId
            ? { members: initial.members, privacy: initial.privacy, emptyHint: initial.emptyHint }
            : { members: [], privacy: null, emptyHint: L("当前无房间。请创建或选择一个房间，也可以用邀请码加入。", "You are not in a room. Create or choose one, or join with an invite code.") },
        );
        break;
      case "room.create":
        state.rooms.push({ id: crypto.randomUUID(), name: m.name as string, isOwner: true, memberCount: 1, maxMembers: 20, onlineCount: null, joined: L("11 月 11 日加入", "Joined Nov 11") });
        state.selectedRoomId = state.rooms.at(-1)!.id;
        break;
      case "room.join":
        if (m.code === "00000000") return { ok: false, error: L("邀请码无效", "The invite code is invalid") };
        state.rooms.push({ id: crypto.randomUUID(), name: L("加入的房间", "Joined room"), isOwner: false, memberCount: 2, maxMembers: 20, onlineCount: null, joined: L("11 月 11 日加入", "Joined Nov 11") });
        break;
      case "room.rename": {
        const r = room(m.roomId);
        if (!r?.isOwner) return { ok: false, error: L("只有所有者可以修改房间名称", "Only the owner can rename the room") };
        r.name = m.name as string;
        break;
      }
      case "room.inviteGet":
        return { ok: true, error: null, data: { code: inviteOf(m.roomId as string) } };
      case "room.inviteCopy": {
        const code = inviteOf(m.roomId as string);
        void navigator.clipboard.writeText(code).catch(() => {});
        return { ok: true, error: null, data: { code } };
      }
      case "room.invite": {
        if (!room(m.roomId)?.isOwner) return { ok: false, error: L("只有所有者可以生成邀请码", "Only the owner can create invite codes") };
        const code = newCode();
        roomInvites.set(m.roomId as string, code);
        void navigator.clipboard.writeText(code).catch(() => {});
        return { ok: true, error: null, data: { code } };
      }
      case "room.privacy": {
        const levels = m.levels as Record<string, PrivacyLevel>;
        state.privacy?.rows.forEach((row) => (row.level = levels[row.category] ?? row.level));
        break;
      }
      case "room.shareStats":
        if (state.privacy) Object.assign(state.privacy, { shareStats: m.enabled as boolean, statsDays: m.days as number });
        break;
      case "room.sharePausedStats":
        if (state.privacy) state.privacy.sharePausedStats = m.enabled as boolean;
        break;
      case "stats.query": {
        // 样例只有「今天」所在的日、周、月；翻页时返回同一份。
        await new Promise((resolve) => setTimeout(resolve, m.userId ? 600 : 80));
        const key = `${m.userId ? "member-" : ""}${m.scale}-${m.rankBy}`;
        const hours = m.scale === "Day" && !m.userId ? (m.hours as number[] | null) : null;
        // 样例里只有「本周只看游戏」一页，其他筛选返回不筛选的那页。
        // 选中时段只有 9–12 点和 9–11、14–16 点两种，其他选择返回 9–12 点那页。
        const view = hours?.length
          ? (views[`${key}-h${hours.join(",")}`] ?? views[`${key}-h9,10,11`])
          : (views[`${key}-${m.filter}`] ?? views[key]);
        return view
          ? { ok: true, error: null, data: view }
          : { ok: false, error: L("对方不在线、当前不在此房间，或没有响应。", "This member is offline, not in this room, or did not respond.") };
      }
      case "stats.app": {
        // 样例只有每个应用在「今天」所在的日、周、月的详情；标题详情返回所属应用的那页，换上标题。
        await new Promise((resolve) => setTimeout(resolve, 80));
        const view = appViews[`${m.scale}-${m.app}`];
        if (!view) return { ok: false, error: L("读取统计失败", "Could not load stats") };
        const title = m.title as string | null;
        if (!title) return { ok: true, error: null, data: view };
        const rule = view.rule?.kind === "Process" ? { ...view.rule, kind: "AppTitle" as const, pattern: `${view.rule.pattern}/${title.toLowerCase()}` } : view.rule;
        return { ok: true, error: null, data: { ...view, name: title, sub: `${view.name} · ${view.sub}`, titles: [], rule } };
      }
      case "stats.export":
        return { ok: true, error: null, data: { message: L("已导出到 C:\\Users\\alice\\Documents\\DeskPal-使用统计-20261111.json", "Exported to C:\\Users\\alice\\Documents\\DeskPal-usage-20261111.json") } };
      case "stats.import":
        return { ok: true, error: null, data: { message: L("已导入 1280 段记录、0 条按天汇总。", "Imported 1280 records and 0 daily totals.") } };
      case "stats.clear":
        if (state.usage) state.usage.viewers = [];
        break;
      // 样例里 PIN 码固定为 12345678。
      case "stats.sync.enable":
        await new Promise((resolve) => setTimeout(resolve, 500));
        if (state.usage) state.usage.sync = { enabled: true, needsPin: false, error: null };
        break;
      case "stats.sync.unlock":
        await new Promise((resolve) => setTimeout(resolve, 500));
        if (m.pin !== "12345678") return { ok: false, error: L("PIN 码不正确", "Incorrect PIN") };
        if (state.usage) state.usage.sync = { enabled: true, needsPin: false, error: null };
        break;
      case "stats.sync.disable":
        if (state.usage) state.usage.sync = { enabled: false, needsPin: false, error: null };
        break;
      case "stats.folder.choose":
        return { ok: true, error: null, data: { path: folderPath, existing: params.has("existing") } };
      case "stats.folder.enable":
        await new Promise((resolve) => setTimeout(resolve, 500));
        if (params.has("existing") && m.pin !== "12345678") return { ok: false, error: L("PIN 码不正确", "Incorrect PIN") };
        if (state.usage) state.usage.folder = { enabled: true, path: m.path as string, unavailable: false };
        break;
      case "stats.folder.stop":
        if (state.usage) state.usage.folder = { enabled: false, path: null, unavailable: false };
        break;
      case "noTitle.add": {
        const kind = m.kind as NoTitleKind;
        const pattern = (m.pattern as string).trim();
        const label = (english ? { App: "App", Process: "Process", Domain: "Site", Title: "Title contains" } : { App: "应用", Process: "进程", Domain: "网址", Title: "标题含" })[kind];
        const rules = (state.noTitleRules ??= []);
        if (!rules.some((r) => r.kind === kind && r.pattern === pattern)) rules.push({ kind, pattern, label: `${label} · ${pattern}` });
        break;
      }
      case "noTitle.remove":
        state.noTitleRules = (state.noTitleRules ?? []).filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        break;
      case "personal.sync.enable":
        await new Promise((resolve) => setTimeout(resolve, 500));
        state.settings.personal = { accountSync: true, folder: null, folderUnavailable: false };
        break;
      case "personal.sync.disable":
        state.settings.personal = { accountSync: false, folder: null, folderUnavailable: false };
        break;
      case "personal.folder.choose":
        state.settings.personal = { accountSync: false, folder: folderPath, folderUnavailable: false };
        break;
      case "personal.folder.stop":
        state.settings.personal = { accountSync: false, folder: null, folderUnavailable: false };
        break;
      case "personal.export":
        return { ok: true, error: null, data: { message: L("已导出到 C:\\Users\\alice\\Documents\\DeskPal-个人数据-20261119.json", "Exported to C:\\Users\\alice\\Documents\\DeskPal-personal-20261119.json") } };
      case "personal.import":
        return { ok: true, error: null, data: { message: L("已导入 12 条分类、3 条不记录标题的规则。", "Imported 12 categories and 3 no-title rules.") } };
      case "room.transfer": {
        const r = room(m.roomId);
        if (r) r.isOwner = false;
        break;
      }
      case "room.kick": {
        const r = room(m.roomId);
        if (!r?.isOwner) return { ok: false, error: L("只有所有者可以踢出成员", "Only the owner can remove members") };
        state.members = state.members.filter((member) => member.userId !== m.userId);
        r.memberCount = Math.max(1, r.memberCount - 1);
        break;
      }
      case "room.leave":
      case "room.dissolve":
        state.rooms = state.rooms.filter((r) => r.id !== m.roomId);
        state.selectedRoomId = state.rooms[0]?.id ?? null;
        if (!state.selectedRoomId) {
          state.members = [];
          state.privacy = null;
          state.emptyHint = L("当前无房间。请创建或选择一个房间，也可以用邀请码加入。", "You are not in a room. Create or choose one, or join with an invite code.");
        }
        break;
      case "member.block":
      case "member.unblock": {
        const member = state.members.find((x) => x.userId === m.userId);
        if (!member) return { ok: false, error: L("该成员不在房间中", "This member is not in the room") };
        member.blocked = m.type === "member.block";
        member.pinned = false;
        member.online = false;
        member.trailing = true;
        member.client = null;
        member.meta = "";
        member.view = { headline: member.blocked ? L("已屏蔽", "Blocked") : L("离线", "Offline"), app: null, detail: null, tone: "offline" };
        sortMembers();
        break;
      }
      case "member.pin":
      case "member.unpin": {
        const member = state.members.find((x) => x.userId === m.userId);
        if (!member) return { ok: false, error: L("该成员不在当前房间", "This member is not in the current room") };
        member.pinned = m.type === "member.pin";
        break;
      }
      case "member.reorder": {
        const current = state.members.map((x) => x.userId);
        const offline = new Set(state.members.filter((x) => x.trailing).map((x) => x.userId));
        const all = [...memberOrder.filter((id) => current.includes(id)), ...current.filter((id) => !memberOrder.includes(id))];
        const dragged = [...new Set(m.userIds as string[])].filter((id) => all.includes(id));
        dragged.push(...all.filter((id) => !dragged.includes(id)));
        const online = dragged.filter((id) => !offline.has(id));
        const away = dragged.filter((id) => offline.has(id));
        memberOrder = all.map((id) => (offline.has(id) ? away.shift()! : online.shift()!));
        sortMembers();
        break;
      }
      case "presence.pause":
        state.me.paused = m.paused as boolean;
        break;
      case "settings.autoStart":
        state.settings.autoStart = m.enabled as boolean;
        break;
      case "settings.topmost":
        state.settings.topmost = m.enabled as boolean;
        break;
      case "settings.theme":
        state.settings.theme = m.theme === "light" || m.theme === "dark" ? m.theme : "system";
        break;
      case "settings.language": {
        // 宿主生成的文字来自对应语言的样例，所以换语言时带上 lang 重新打开。
        const resolved = m.language === "system" ? (navigator.language.startsWith("zh") ? "zh-CN" : "en") : m.language;
        const next = new URLSearchParams(location.search);
        next.set("lang", resolved as string);
        setTimeout(() => (location.search = next.toString()), 100);
        break;
      }
      case "settings.idleMinutes":
        if ([1, 5, 10, 15, 30].includes(m.minutes as number)) state.settings.idleMinutes = m.minutes as number;
        break;
      case "settings.switchDelay":
        if ([0, 3, 5, 10, 30].includes(m.seconds as number)) state.settings.switchDelaySeconds = m.seconds as number;
        break;
      case "settings.closeAction":
        state.settings.closeAction = m.action === "minimize" || m.action === "tray" || m.action === "quit" ? m.action : "ask";
        break;
      case "update.beta":
        state.settings.receiveBeta = m.enabled as boolean;
        break;
      case "update.check": {
        const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
        state.settings.updateActivity = L("正在检查更新…", "Checking for updates…");
        pushState();
        await wait(400);
        state.settings.updateActivity = L("正在下载新版本 0.2.0（40%）", "Downloading version 0.2.0 (40%)");
        pushState();
        await wait(1600);
        state.settings.updateActivity = null;
        pushState();
        if (await ask("confirm", L("已下载新版本 0.2.0。要立即重启并更新吗？", "Version 0.2.0 has been downloaded. Restart and update now?"))) {
          await ask("info", L("（模拟）客户端会退出并安装更新。", "(Mock) The client would exit and install the update."));
        }
        break;
      }
      case "presence.correct":
        state.correction.personal = true;
        state.correction.keyword = (m.keyword as string | null | undefined) ?? null;
        state.me.source = L(`「${state.correction.target}」按你的分类识别。`, `${state.correction.target}: matched your category.`);
        break;
      case "presence.resetCorrection":
        state.correction.personal = false;
        state.correction.keyword = null;
        state.me.source = L(`「${state.correction.target}」按默认分类识别。`, `${state.correction.target}: matched a default category.`);
        break;
      case "rules.set": {
        const name = (m.name as string | null) ?? (m.pattern as string);
        const option = state.correction.options.find((o) => o.value === m.category);
        const keyword = typeof m.keyword === "string" ? m.keyword.toLowerCase() : null;
        const pattern = m.kind === "AppTitle" && keyword ? `${String(m.pattern).split("/")[0]}/${keyword}` : String(m.pattern);
        state.myRules = state.myRules.filter((r) => !(r.kind === m.kind && r.pattern === pattern));
        state.myRules.unshift({
          kind: m.kind as PanelState["myRules"][number]["kind"],
          pattern,
          name,
          kindName: m.kind === "Domain" ? L("网站", "Website") : m.kind === "AppTitle" ? L("标题包含", "Title contains") : L("应用", "App"),
          category: m.category as string,
          categoryName: option?.name ?? (m.category as string),
          shown: m.kind === "AppTitle" ? pattern.slice(pattern.indexOf("/") + 1) : pattern,
        });
        state.unclassified = state.unclassified.filter((u) => u.pattern !== m.pattern);
        break;
      }
      case "rules.delete":
        state.myRules = state.myRules.filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        break;
      case "me.rename":
        state.me.displayName = m.name as string;
        break;
      case "settings.shareUsage":
        state.settings.shareUsage = m.enabled as boolean;
        break;
      case "changelog.seen":
        state.whatsNew = null;
        break;
      case "logs.open":
        return { ok: true, error: null };
      // 插件只粗略模拟开关和排序，不重新计算谁提供哪一项。
      case "plugins.setEnabled": {
        const plugin = state.settings.plugins?.items.find((p) => p.id === String(m.id));
        if (plugin) {
          plugin.enabled = m.enabled as boolean;
          plugin.state = plugin.enabled ? "running" : "disabled";
        }
        break;
      }
      case "plugins.move": {
        const items = state.settings.plugins?.items;
        const index = items?.findIndex((p) => p.id === String(m.id)) ?? -1;
        if (items && index >= 0) {
          const target = Math.max(0, Math.min(items.length - 1, index + (m.offset as number)));
          items.splice(target, 0, ...items.splice(index, 1));
        }
        break;
      }
      case "plugins.refresh":
      case "plugins.openFolder":
      case "plugins.action":
        return { ok: true, error: null };
      case "update.restart":
      case "auth.logout":
        break;
      default:
        return { ok: false, error: L(`不支持的操作：${m.type}`, `Unsupported action: ${m.type}`) };
    }
    pushState();
    return { ok: true, error: null };
  }

  return {
    post(message) {
      const m = message as Incoming;
      window.__sent!.push(m);
      console.log("[mock] ->", m);
      if (m.type === "ready") {
        if (login)
          emit({
            type: "login",
            userName: "alice",
            notice: params.has("signedOut") ? L("账号已在另一台电脑登录，这台电脑已退出。请重新登录。", "Your account was signed in on another computer, so this one was signed out. Sign in again to continue.") : null,
          });
        else if (card) {
          const member = state.members[0];
          if (member) {
            const detail = [member.view.app, member.view.detail].filter(Boolean).join(" · ") || null;
            emit({ type: "card", member, detail, theme: state.settings.theme, locale: state.locale });
          }
          return;
        } else pushState();
        const pending = state.me.pending;
        if (pending) {
          setTimeout(() => {
            state.me.view = pending.view;
            state.me.pending = null;
            state.me.full = pending.full;
            state.me.meta = L("刚刚开始", "Just started");
            if (state.me.visible) state.me.visible = pending.view.headline;
            pushState();
          }, pending.remainingMs + 300);
        }
        return;
      }
      if (m.type.startsWith("window.")) return;
      if (m.type === "dialog.answer") {
        dialogs.get(m.dialogId as number)?.(m.ok as boolean);
        dialogs.delete(m.dialogId as number);
        return;
      }
      void handle(m).then((r) => emit({ type: "result", id: m.id!, ...r }));
    },
    listen(listener) {
      listeners.push(listener);
    },
  };
}
