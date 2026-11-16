import type { ComponentChildren, RefObject } from "preact";
import { useEffect, useLayoutEffect, useRef, useState } from "preact/hooks";
import type { Bridge, PresenceView, Tone } from "./bridge";
import { t } from "./i18n";

// Segoe Fluent Icons（Windows 11）/ Segoe MDL2 Assets（Windows 10）里的字形。
const glyphs = {
  people: "\uE716",
  group: "\uE902",
  home: "\uE80F",
  settings: "\uE713",
  minimize: "\uE921",
  close: "\uE8BB",
  chevronDown: "\uE70D",
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
} as const;

export type IconName = keyof typeof glyphs;

export function Icon({ name, class: cls }: { name: IconName; class?: string }) {
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

export function Avatar({ name, tone }: { name: string; tone: PresenceView["tone"] }) {
  const initial = Array.from(name.trim())[0]?.toUpperCase() ?? "?";
  return (
    <span class={`avatar tv-${tone}`} aria-hidden="true">
      {initial}
    </span>
  );
}

const roomTones = ["work", "chat", "relax", "game", "browse", "other"] as const;

/** 房间的颜色按 id 固定，同一个房间在哪里都是同一种颜色。 */
export function roomTone(id: string): Tone {
  let hash = 0;
  for (const ch of id) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return roomTones[Math.abs(hash) % roomTones.length];
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
