// 浏览器里开发用的模拟宿主。初始状态来自 C# 测试生成的 fixtures，命令的效果在这里粗略模拟。
// 地址参数：?mock=default|no-room|guest 选样例；?fail 让所有命令失败；?page=login 打开登录页。
// 发出的命令记在 window.__sent，方便检查。

import type { CommandResult, HostMessage, PanelState, PrivacyLevel, Transport } from "../bridge";
import defaultState from "./fixtures/default.json";
import guestState from "./fixtures/guest.json";
import noRoomState from "./fixtures/no-room.json";

declare global {
  interface Window {
    __sent?: unknown[];
  }
}

const fixtures: Record<string, PanelState> = {
  default: defaultState as PanelState,
  "no-room": noRoomState as PanelState,
  guest: guestState as PanelState,
};

type Incoming = { id?: number; type: string; [key: string]: unknown };

export function createMockTransport(params: URLSearchParams): Transport {
  const listeners: ((m: HostMessage) => void)[] = [];
  const state: PanelState = structuredClone(fixtures[params.get("mock") ?? "default"] ?? fixtures.default);
  const fail = params.has("fail");
  const login = params.get("page") === "login";
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

  async function handle(m: Incoming): Promise<CommandResult> {
    if (fail) return { ok: false, error: "服务器暂时不可用，请稍后再试" };
    switch (m.type) {
      case "auth.login":
        return m.password === "wrong"
          ? { ok: false, error: "用户名或密码错误" }
          : { ok: true, error: null };
      case "auth.register":
        return { ok: true, error: null };
      case "room.select":
        state.selectedRoomId = m.roomId as string;
        break;
      case "room.create":
        state.rooms.push({ id: crypto.randomUUID(), name: m.name as string, isOwner: true, memberCount: 1, maxMembers: 50 });
        state.selectedRoomId = state.rooms.at(-1)!.id;
        break;
      case "room.join":
        if (m.code === "00000000") return { ok: false, error: "邀请码无效或已过期" };
        state.rooms.push({ id: crypto.randomUUID(), name: "加入的房间", isOwner: false, memberCount: 2, maxMembers: 50 });
        break;
      case "room.rename": {
        const r = room(m.roomId);
        if (!r?.isOwner) return { ok: false, error: "只有所有者可以修改房间名称" };
        r.name = m.name as string;
        break;
      }
      case "room.invite":
        return {
          ok: true,
          error: null,
          data: { code: "K7Q2M9XA", message: "邀请码已复制到剪贴板，11 月 12 日 15:00 前有效。重新生成后旧邀请码失效，被踢出的人可以用新邀请码加入。" },
        };
      case "room.privacy": {
        const levels = m.levels as Record<string, PrivacyLevel>;
        state.privacy?.rows.forEach((row) => (row.level = levels[row.category] ?? row.level));
        break;
      }
      case "room.transfer": {
        const r = room(m.roomId);
        if (r) r.isOwner = false;
        break;
      }
      case "room.kick": {
        const r = room(m.roomId);
        if (!r?.isOwner) return { ok: false, error: "只有所有者可以踢出成员" };
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
          state.emptyHint = "还没有房间。到「房间」页创建房间，或者用邀请码加入。";
        }
        break;
      case "presence.pause":
        state.me.paused = m.paused as boolean;
        break;
      case "presence.manual":
        state.me.manualText = (m.text as string | null) ?? null;
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
      case "settings.closeAction":
        state.settings.closeAction = m.action === "minimize" || m.action === "tray" ? m.action : "ask";
        break;
      case "update.beta":
        state.settings.receiveBeta = m.enabled as boolean;
        break;
      case "update.check": {
        const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
        state.settings.updateActivity = "正在检查更新…";
        pushState();
        await wait(400);
        state.settings.updateActivity = "正在下载新版本 0.2.0（40%）";
        pushState();
        await wait(1600);
        state.settings.updateActivity = null;
        pushState();
        if (await ask("confirm", "已下载新版本 0.2.0。要立即重启并更新吗？")) {
          await ask("info", "（模拟）客户端会退出并安装更新。");
        }
        break;
      }
      case "presence.correct":
        state.correction.personal = true;
        state.me.source = `「${state.correction.target}」按你设置的分类识别。`;
        break;
      case "presence.resetCorrection":
        state.correction.personal = false;
        state.me.source = `「${state.correction.target}」按默认分类识别。`;
        break;
      case "rules.set": {
        const name = (m.name as string | null) ?? (m.pattern as string);
        const option = state.correction.options.find((o) => o.value === m.category);
        state.myRules = state.myRules.filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        state.myRules.unshift({
          kind: m.kind as PanelState["myRules"][number]["kind"],
          pattern: m.pattern as string,
          name,
          kindName: m.kind === "Domain" ? "网站" : "应用",
          category: m.category as string,
          categoryName: option?.name ?? (m.category as string),
        });
        state.unclassified = state.unclassified.filter((u) => u.pattern !== m.pattern);
        break;
      }
      case "rules.delete":
        state.myRules = state.myRules.filter((r) => !(r.kind === m.kind && r.pattern === m.pattern));
        break;
      case "settings.shareUsage":
        state.settings.shareUsage = m.enabled as boolean;
        break;
      case "member.poke":
      case "update.restart":
      case "auth.logout":
        break;
      default:
        return { ok: false, error: `不支持的操作：${m.type}` };
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
        if (login) emit({ type: "login", serverUrl: "https://deskpal.hafuhafu.cn:36443", userName: "alice" });
        else pushState();
        const pending = state.me.pending;
        if (pending) {
          setTimeout(() => {
            state.me.view = pending.view;
            state.me.pending = null;
            state.me.meta = "刚刚开始";
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
