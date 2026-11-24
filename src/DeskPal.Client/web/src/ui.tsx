import type { ComponentChildren, RefObject } from "preact";
import { useEffect, useLayoutEffect, useRef, useState } from "preact/hooks";
import type { Bridge, ClientKind, PresenceView, Tone } from "./bridge";
import { t } from "./i18n";
import { isWindows } from "./platform";

// Segoe Fluent Icons（Windows 11）/ Segoe MDL2 Assets（Windows 10）里的字形。
const glyphs = {
  people: "\uE716",
  group: "\uE902",
  home: "\uE80F",
  settings: "\uE713",
  minimize: "\uE921",
  close: "\uE8BB",
  chevronDown: "\uE70D",
  chevronUp: "\uE70E",
  add: "\uE710",
  edit: "\uE70F",
  delete: "\uE74D",
  link: "\uE71B",
  tag: "\uE8EC",
  undo: "\uE7A7",
  accept: "\uE8FB",
  info: "\uE946",
  warning: "\uE7BA",
  ring: "\uEA8F",
  refresh: "\uE72C",
  timer: "\uE916",
  forward: "\uE72A",
  back: "\uE72B",
  chart: "\uE9D2",
  hide: "\uED1A",
  search: "\uE721",
  pin: "\uE718",
  pinned: "\uE841",
  more: "\uE712",
  block: "\uE8F8",
  remove: "\uE738",
  app: "\uECAA",
  globe: "\uE774",
  text: "\uE8D2",
  check: "\uE73E",
  copy: "\uE8C8",
  filter: "\uE71C",
} as const;

export type IconName = keyof typeof glyphs;

/** 圆：圆心 (x, y)，半径 r。 */
const circle = (x: number, y: number, r: number) => `M${x - r} ${y}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`;
const person = (x: number, y: number) => `${circle(x, y, 2.3)}M${x - 4.3} ${y + 8.2}c0-2.4 1.9-4.3 4.3-4.3s4.3 1.9 4.3 4.3`;
const pin = "M9.6 2.4l4 4-2 1-2.4 2.4.5 2.6-1.1 1.1-5.1-5.1 1.1-1.1 2.6.5 2.4-2.4z";

/**
 * macOS 和 Linux 没有 Segoe Fluent Icons，改用同样含义的线条图标（16×16，描边，跟随文字颜色和字号）。
 * 和上面的字形一一对应，Windows 上不使用。
 */
const paths: Record<IconName, string> = {
  people: `${person(6, 5)}M11 3.2a2.2 2.2 0 0 1 0 4.2M12.4 9.6c1.3.6 2.1 2 2.1 3.6`,
  group: `${person(8, 4.5)}${circle(3.2, 6.6, 1.6)}${circle(12.8, 6.6, 1.6)}M1 12.5c0-1.6 1-2.8 2.4-3.1M15 12.5c0-1.6-1-2.8-2.4-3.1`,
  home: "M2 7.5 8 2.5l6 5M3.5 6.5V13.5h3.3v-4h2.4v4h3.3V6.5",
  settings: `${circle(8, 8, 2.2)}${circle(8, 8, 4.6)}M8 1.5v1.9M8 12.6v1.9M1.5 8h1.9M12.6 8h1.9M3.4 3.4l1.3 1.3M11.3 11.3l1.3 1.3M3.4 12.6l1.3-1.3M11.3 4.7l1.3-1.3`,
  minimize: "M3.5 8h9",
  close: "M4 4l8 8M12 4l-8 8",
  chevronDown: "M4 6l4 4 4-4",
  chevronUp: "M4 10l4-4 4 4",
  add: "M8 3v10M3 8h10",
  edit: "M10.6 2.6l2.8 2.8L6 12.8H3.2V10zM9.2 4l2.8 2.8",
  delete: "M3 4.5h10M6.4 4.5V2.8h3.2v1.7M4.5 4.5l.7 9h5.6l.7-9M7 7v4.3M9 7v4.3",
  link: "M7 9.2a3 3 0 0 0 4.2 0l2-2a3 3 0 0 0-4.2-4.2l-.8.8M9 6.8a3 3 0 0 0-4.2 0l-2 2a3 3 0 0 0 4.2 4.2l.8-.8",
  tag: `M2.5 2.5h5l6 6-5 5-6-6z${circle(5.5, 5.5, 0.8)}`,
  undo: "M5 3 2.5 5.5 5 8M2.5 5.5H10a3.5 3.5 0 0 1 0 7H6",
  accept: "M3 8.5l3 3 7-7",
  info: `${circle(8, 8, 6)}M8 7.2v4M8 4.9v.1`,
  warning: "M8 2.3l6.2 11H1.8zM8 6.4v3.4M8 11.6v.1",
  ring: "M4 11V7a4 4 0 0 1 8 0v4l1.2 1.4H2.8zM6.5 14h3",
  refresh: "M13 8a5 5 0 1 1-1.5-3.6M13 2.4v3h-3",
  timer: `${circle(8, 9, 5)}M8 6.3V9l2 1.4M6.4 1.8h3.2`,
  forward: "M6 3.5 10.5 8 6 12.5",
  back: "M10 3.5 5.5 8l4.5 4.5",
  chart: "M2.5 13.5h11M4.5 11.2V8.2M7.7 11.2V4.6M10.9 11.2V6.6",
  hide: `M1.8 8s2.3-4 6.2-4 6.2 4 6.2 4-2.3 4-6.2 4-6.2-4-6.2-4z${circle(8, 8, 1.9)}M2.5 2.5l11 11`,
  search: `${circle(7, 7, 4.3)}M10.2 10.2l3.4 3.4`,
  pin: `${pin}M5.4 10.6l-3 3`,
  pinned: `${pin}M5.4 10.6l-3 3`,
  more: "M3.5 8h.01M8 8h.01M12.5 8h.01",
  block: `${circle(8, 8, 5.8)}M3.9 3.9l8.2 8.2`,
  remove: `${circle(8, 8, 5.8)}M5.2 8h5.6`,
  app: "M2.5 3.5h11v9h-11zM2.5 6h11M4.3 4.8h.01M5.8 4.8h.01",
  globe: `${circle(8, 8, 6)}M2 8h12M8 2c2 2.2 2 9.8 0 12M8 2c-2 2.2-2 9.8 0 12`,
  text: "M3 4V3h10v1M8 3v10M6 13h4",
  check: "M3 8.5l3 3 7-7",
  copy: "M5.5 5.5h8v8h-8zM10.5 5.5v-3h-8v8h3",
  filter: "M2.5 3h11L9.3 8v4.6l-2.6 1.4V8z",
};

export function Icon({ name, class: cls }: { name: IconName; class?: string }) {
  if (!isWindows()) {
    return (
      <svg
        class={cls ? `icon svg-icon ${cls}` : "icon svg-icon"}
        aria-hidden="true"
        viewBox="0 0 16 16"
        width="1em"
        height="1em"
        fill={name === "pinned" ? "currentColor" : "none"}
        stroke="currentColor"
        stroke-width={name === "more" ? 2.4 : 1.4}
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d={paths[name]} />
      </svg>
    );
  }

  return (
    <span class={cls ? `icon ${cls}` : "icon"} aria-hidden="true">
      {glyphs[name]}
    </span>
  );
}

/** 自绘标题栏。空白处可以拖动窗口（app-region: drag），按钮区域不参与拖动。不传 title 时显示 DeskPal 字标。 */
export function TitleBar({ bridge, title, children, onClose }: { bridge: Bridge; title?: string; children?: ComponentChildren; onClose?: () => void }) {
  return (
    <header class="titlebar">
      {title ? (
        <span class="app-title">{title}</span>
      ) : (
        <span class="wordmark" aria-label="DeskPal">
          Desk<span class="wordmark-accent">Pal</span>
        </span>
      )}
      <div class="titlebar-content">{children}</div>
      <div class="caption-buttons">
        <button class="caption" aria-label={t("common.minimize")} title={t("common.minimize")} onClick={() => bridge.notify({ type: "window.minimize" })}>
          <Icon name="minimize" />
        </button>
        <button class="caption close" aria-label={t("common.close")} title={t("common.close")} onClick={() => (onClose ? onClose() : bridge.notify({ type: "window.close" }))}>
          <Icon name="close" />
        </button>
      </div>
    </header>
  );
}

export function Switch({ checked, label, disabled, onChange }: { checked: boolean; label: string; disabled?: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      role="switch"
      class="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      <span class="switch-text">{checked ? t("common.on") : t("common.off")}</span>
      <span class="switch-track">
        <span class="switch-thumb" />
      </span>
    </button>
  );
}

/** 说明气泡：贴在 anchor 下方，放不下时放上方，用 fixed 定位避免被滚动区域裁切；滚动或窗口失焦时收起。 */
function useBubble(open: boolean, close: () => void) {
  const anchorRef = useRef<HTMLElement>(null);
  const bubbleRef = useRef<HTMLSpanElement>(null);

  useLayoutEffect(() => {
    const target = anchorRef.current;
    const bubble = bubbleRef.current;
    if (!open || !target || !bubble) return;
    const anchor = target.getBoundingClientRect();
    const margin = 8;
    const gap = 6;
    const width = bubble.offsetWidth;
    const height = bubble.offsetHeight;
    let left = anchor.left;
    const maxLeft = window.innerWidth - margin - width;
    if (left > maxLeft) left = Math.max(margin, maxLeft);
    const below = anchor.bottom + gap;
    const above = anchor.top - gap - height;
    const top = below + height > window.innerHeight - margin && above >= margin ? above : below;
    bubble.style.left = `${left}px`;
    bubble.style.top = `${top}px`;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    window.addEventListener("scroll", close, true);
    window.addEventListener("blur", close);
    return () => {
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("blur", close);
    };
  }, [open]);

  return { anchorRef, bubbleRef };
}

/** 标题旁的圆形问号。悬停或键盘聚焦时显示说明。 */
export function Hint({ text }: { text: string }) {
  const [open, setOpen] = useState(false);
  const { anchorRef, bubbleRef } = useBubble(open, () => setOpen(false));

  return (
    <span class="hint">
      <button
        type="button"
        class="hint-button"
        ref={anchorRef as RefObject<HTMLButtonElement>}
        aria-label={t("common.hintLabel", { text })}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
      >
        ?
      </button>
      {open && (
        <span class="hint-bubble" ref={bubbleRef} role="tooltip">
          {text}
        </span>
      )}
    </span>
  );
}

/** 悬停整块内容时显示气泡。指到里面的问号时让给问号自己的说明。 */
export function HoverTip({ text, class: cls, children }: { text: string | null; class?: string; children: ComponentChildren }) {
  const [open, setOpen] = useState(false);
  const { anchorRef, bubbleRef } = useBubble(open, () => setOpen(false));

  return (
    <div
      class={cls}
      ref={anchorRef as RefObject<HTMLDivElement>}
      onMouseOver={(e) => setOpen(!!text && !(e.target as Element).closest(".hint"))}
      onMouseLeave={() => setOpen(false)}
    >
      {children}
      {open && text && (
        <span class="hint-bubble" ref={bubbleRef} role="tooltip">
          {text}
        </span>
      )}
    </div>
  );
}

/** 设置页的一行：左边是名称，说明放在问号里；动态状态仍直接写在名称下面。右边是控件。 */
export function SettingRow({ icon, title, description, hint, children }: { icon?: IconName; title: string; description?: ComponentChildren; hint?: string; children?: ComponentChildren }) {
  return (
    <div class="setting-row">
      {icon && <Icon name={icon} class="setting-icon" />}
      <div class="setting-text">
        <div class="setting-title">
          <span>{title}</span>
          {hint && <Hint text={hint} />}
        </div>
        {description && <p class="setting-desc">{description}</p>}
      </div>
      {children && <div class="setting-control">{children}</div>}
    </div>
  );
}

export function Section({ title, aside, children, label }: { title?: ComponentChildren; aside?: ComponentChildren; children: ComponentChildren; label?: string }) {
  return (
    <section class="section" aria-label={label ?? (typeof title === "string" ? title : undefined)}>
      {(title || aside) && (
        <div class="section-head">
          {title && <h2>{title}</h2>}
          {aside}
        </div>
      )}
      {children}
    </section>
  );
}

/** 传 client 时在头像右下角显示客户端标志，位置不随名字长短变化。 */
export function Avatar({ name, tone, client }: { name: string; tone: PresenceView["tone"]; client?: ClientKind | null }) {
  const initial = Array.from(name.trim())[0]?.toUpperCase() ?? "?";
  return (
    <span class={`avatar tv-${tone}`}>
      <span aria-hidden="true">{initial}</span>
      <ClientIcon client={client} />
    </span>
  );
}

const idTones = ["work", "chat", "relax", "game", "browse", "other"] as const;

function idTone(id: string): Tone {
  let hash = 0;
  for (const ch of id) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return idTones[Math.abs(hash) % idTones.length];
}

/** 房间的颜色按 id 固定，同一个房间在哪里都是同一种颜色。 */
export const roomTone = idTone;

/** 房间页成员头像的颜色按用户 id 固定，不表示状态。 */
export const memberTone = idTone;

const clientLogos: Record<ClientKind, { viewBox: string; path: string }> = {
  windows: { viewBox: "0 0 16 16", path: "M1 1h6.3v6.3H1zM8.7 1H15v6.3H8.7zM1 8.7h6.3V15H1zM8.7 8.7H15V15H8.7z" },
  mac: {
    viewBox: "0 0 24 24",
    path: "M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 3.014-2.117 3.675-.546 9.103 1.519 12.09 1.013 1.454 2.208 3.09 3.792 3.039 1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857-.026-3.04 2.48-4.494 2.597-4.559-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052-2.662.805-3.532 1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701",
  },
  // Linux：终端提示符 >_
  linux: { viewBox: "0 0 16 16", path: "M2 3.2 3.4 1.8 9.6 8 3.4 14.2 2 12.8 6.8 8zM9 12.2h5v2H9z" },
};

/** 客户端标志（Windows、macOS、Linux），头像右下角的小圆标，悬停显示名称。 */
function ClientIcon({ client }: { client: ClientKind | null | undefined }) {
  const logo = client && clientLogos[client];
  if (!logo) return null;
  const label = t(client === "mac" ? "client.mac" : client === "linux" ? "client.linux" : "client.windows");
  return (
    <span class={`client-icon ${client}`} role="img" aria-label={label} title={label}>
      <svg viewBox={logo.viewBox} aria-hidden="true">
        <path d={logo.path} />
      </svg>
    </span>
  );
}

/** 分类的颜色，和统计页的类别图例一致（StatsViewBuilder.CategoryLegend）。 */
export function categoryTone(category: string): Tone {
  const tones: Record<string, Tone> = { Work: "work", Communication: "chat", Video: "relax", Game: "game", Browsing: "browse" };
  return tones[category] ?? "other";
}

/** 房间的首字母方块。不传 id 表示无房间。 */
export function RoomBadge({ id, name, size }: { id?: string; name?: string; size?: "small" | "large" }) {
  const cls = size ? `room-badge ${size}` : "room-badge";
  if (!id) return <span class={`${cls} empty`} aria-hidden="true" />;
  return (
    <span class={`${cls} tv-${roomTone(id)}`} aria-hidden="true">
      {Array.from(name?.trim() ?? "")[0]?.toUpperCase() ?? "?"}
    </span>
  );
}

/** 点到 root 外面、按 Esc 或窗口失焦时关闭弹出的菜单。 */
function useDismiss(open: boolean, close: () => void, root: RefObject<HTMLElement>) {
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!root.current?.contains(e.target as Node)) close();
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && close();
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    window.addEventListener("blur", close);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("blur", close);
    };
  }, [open]);
}

export type MenuEntry = { label: string; icon?: IconName; danger?: boolean; onSelect: () => void };

/** 行尾的「更多」按钮。菜单用 fixed 定位、和按钮右对齐，避免被滚动区域裁切；滚动时收起。 */
export function MoreMenu({ label, items }: { label: string; items: MenuEntry[] }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const close = () => setOpen(false);
  useDismiss(open, close, root);

  useLayoutEffect(() => {
    const anchor = button.current?.getBoundingClientRect();
    const popup = menu.current;
    if (!open || !anchor || !popup) return;
    const margin = 8;
    const left = Math.max(margin, Math.min(anchor.right - popup.offsetWidth, window.innerWidth - margin - popup.offsetWidth));
    const below = anchor.bottom + 4;
    const above = anchor.top - 4 - popup.offsetHeight;
    popup.style.left = `${left}px`;
    popup.style.top = `${below + popup.offsetHeight > window.innerHeight - margin && above >= margin ? above : below}px`;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    window.addEventListener("scroll", close, true);
    return () => window.removeEventListener("scroll", close, true);
  }, [open]);

  return (
    <div class="menu-root" ref={root}>
      <button ref={button} class="icon-button" aria-haspopup="menu" aria-expanded={open} aria-label={label} title={t("common.more")} onClick={() => setOpen(!open)}>
        <Icon name="more" />
      </button>
      {open && (
        <div class="flyout context-menu" role="menu" aria-label={label} ref={menu}>
          {items.map((item) => (
            <button
              key={item.label}
              role="menuitem"
              class={item.danger ? "menu-item danger" : "menu-item"}
              onClick={() => {
                close();
                item.onSelect();
              }}
            >
              <span class="menu-icon">{item.icon && <Icon name={item.icon} />}</span>
              <span class="menu-text">{item.label}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/** 点击后弹出的菜单，用于标题栏的房间切换。 */
export function Flyout({ label, button, header, children }: { label: string; button: ComponentChildren; header?: string; children: (close: () => void) => ComponentChildren }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  useDismiss(open, () => setOpen(false), root);

  return (
    <div class="flyout-root" ref={root}>
      <button class="flyout-button" aria-haspopup="menu" aria-expanded={open} aria-label={label} onClick={() => setOpen(!open)}>
        {button}
        <Icon name="chevronDown" class="chevron" />
      </button>
      {open && (
        <div class="flyout" role="menu" aria-label={label}>
          {header && <div class="menu-header">{header}</div>}
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  );
}
