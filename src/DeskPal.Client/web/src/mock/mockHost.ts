// 浏览器里开发用的模拟宿主。初始状态来自 C# 测试生成的 fixtures，命令的效果在这里粗略模拟。
// 地址参数：?mock=default|no-room|guest|updated 选样例；?lang=en 用英文样例；?fail 让所有命令失败；?page=login 打开登录页；?page=card 打开第一个成员的置顶小卡片。
// 发出的命令记在 window.__sent，方便检查。

import type { CommandResult, HostMessage, NoTitleKind, PanelState, PrivacyLevel, SignupInviteItem, StatsView, Transport } from "../bridge";
import defaultState from "./fixtures/default.json";
import statsViews from "./fixtures/stats.json";
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

export function createMockTransport(params: URLSearchParams): Transport {
  const listeners: ((m: HostMessage) => void)[] = [];
  const english = params.get("lang") === "en";
  // 宿主生成的文字按语言给，和 C# 一致。
  const L = (zh: string, en: string) => (english ? en : zh);
  const samples = english ? enFixtures : fixtures;
  const views = (english ? enStatsViews : statsViews) as unknown as Record<string, StatsView>;
  const initial = samples[params.get("mock") ?? "default"] ?? samples.default;
  const state: PanelState = structuredClone(initial);
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
        const code = Array.from({ length: 8 }, () => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[Math.floor(Math.random() * 32)]).join("");
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
        state.rooms.push({ id: crypto.randomUUID(), name: m.name as string, isOwner: true, memberCount: 1, maxMembers: 20 });
        state.selectedRoomId = state.rooms.at(-1)!.id;
        break;
      case "room.join":
        if (m.code === "00000000") return { ok: false, error: L("邀请码无效或已过期", "The invite code is invalid or has expired") };
        state.rooms.push({ id: crypto.randomUUID(), name: L("加入的房间", "Joined room"), isOwner: false, memberCount: 2, maxMembers: 20 });
        break;
      case "room.rename": {
        const r = room(m.roomId);
        if (!r?.isOwner) return { ok: false, error: L("只有所有者可以修改房间名称", "Only the owner can rename the room") };
        r.name = m.name as string;
        break;
      }
      case "room.invite":
        return {
          ok: true,
          error: null,
          data: {
            code: "K7Q2M9XA",
            message: L(
              "邀请码已复制，11 月 12 日 15:00 前有效。重新生成后旧邀请码失效，被踢出的成员可使用新邀请码加入。",
              "Invite code copied. Valid until Nov 12, 15:00. A new code replaces the old one. Removed members can rejoin with the new code.",
            ),
          },
        };
      case "room.privacy": {
        const levels = m.levels as Record<string, PrivacyLevel>;
        state.privacy?.rows.forEach((row) => (row.level = levels[row.category] ?? row.level));
        break;
      }
      case "room.shareStats":
        if (state.privacy) Object.assign(state.privacy, { shareStats: m.enabled as boolean, statsDays: m.days as number });
        break;
      case "stats.query": {
        // 样例只有「今天」所在的日、周、月；翻页时返回同一份。
        await new Promise((resolve) => setTimeout(resolve, m.userId ? 600 : 80));
        const key = `${m.userId ? "member-" : ""}${m.scale}-${m.granularity}`;
        const view = views[key];
        return view
          ? { ok: true, error: null, data: view }
          : { ok: false, error: L("对方不在线、当前不在此房间，或没有响应。", "This member is offline, not in this room, or did not respond.") };
      }
      case "stats.export":
        return { ok: true, error: null, data: { message: L("已导出到 C:\\Users\\alice\\Documents\\DeskPal-使用统计-20261111.json", "Exported to C:\\Users\\alice\\Documents\\DeskPal-usage-20261111.json") } };
      case "stats.import":
        return { ok: true, error: null, data: { message: L("已导入 1280 段记录、0 条按天汇总。", "Imported 1280 records and 0 daily totals.") } };
      case "stats.clear":
        if (state.usage) state.usage.viewers = [];
        break;
      case "stats.retention":
        if (state.usage) state.usage.retentionDays = m.days as number;
        break;
      case "stats.noTitle.add": {
        const kind = m.kind as NoTitleKind;
        const pattern = (m.pattern as string).trim();
        const label = (english ? { App: "App", Process: "Process", Domain: "Site", Title: "Title contains" } : { App: "应用", Process: "进程", Domain: "网址", Title: "标题含" })[kind];
        if (state.usage && !state.usage.noTitleRules.some((r) => r.kind === kind && r.pattern === pattern))
          state.usage.noTitleRules.push({ kind, pattern, label: `${label} · ${pattern}` });
        break;
      }
      case "stats.noTitle.remove":
        if (state.usage) state.usage.noTitleRules = state.usage.noTitleRules.filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        break;
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
        member.meta = "";
        member.view = { headline: member.blocked ? L("已屏蔽", "Blocked") : L("离线", "Offline"), app: null, detail: null, tone: "offline" };
        break;
      }
      case "member.pin":
      case "member.unpin": {
        const member = state.members.find((x) => x.userId === m.userId);
        if (!member) return { ok: false, error: L("该成员不在当前房间", "This member is not in the current room") };
        member.pinned = m.type === "member.pin";
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
      case "settings.closeAction":
        state.settings.closeAction = m.action === "minimize" || m.action === "tray" ? m.action : "ask";
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
        state.me.source = L(`「${state.correction.target}」按你的分类识别。`, `${state.correction.target}: matched your category.`);
        break;
      case "presence.resetCorrection":
        state.correction.personal = false;
        state.me.source = L(`「${state.correction.target}」按默认分类识别。`, `${state.correction.target}: matched a default category.`);
        break;
      case "rules.set": {
        const name = (m.name as string | null) ?? (m.pattern as string);
        const option = state.correction.options.find((o) => o.value === m.category);
        state.myRules = state.myRules.filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        state.myRules.unshift({
          kind: m.kind as PanelState["myRules"][number]["kind"],
          pattern: m.pattern as string,
          name,
          kindName: m.kind === "Domain" ? L("网站", "Website") : L("应用", "App"),
          category: m.category as string,
          categoryName: option?.name ?? (m.category as string),
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
            serverUrl: "https://deskpal.hafuhafu.cn:36443",
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
