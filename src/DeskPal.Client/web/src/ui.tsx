import type { ComponentChildren } from "preact";
import { useEffect, useLayoutEffect, useRef, useState } from "preact/hooks";
import type { Bridge, PresenceView } from "./bridge";

// Segoe Fluent Icons（Windows 11）/ Segoe MDL2 Assets（Windows 10）里的字形。
const glyphs = {
  people: "\uE716",
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
  hide: "\uED1A",
  search: "\uE721",
  pin: "\uE718",
  pinned: "\uE841",
} as const;

export type IconName = keyof typeof glyphs;

export function Icon({ name, class: cls }: { name: IconName; class?: string }) {
  return (
    <span class={cls ? `icon ${cls}` : "icon"} aria-hidden="true">
      {glyphs[name]}
    </span>
  );
}

/** 自绘标题栏。空白处可以拖动窗口（app-region: drag），按钮区域不参与拖动。 */
export function TitleBar({ bridge, title, children, onClose }: { bridge: Bridge; title: string; children?: ComponentChildren; onClose?: () => void }) {
  return (
    <header class="titlebar">
      <span class="app-mark" aria-hidden="true">
        D
      </span>
      <span class="app-title">{title}</span>
      <div class="titlebar-content">{children}</div>
      <div class="caption-buttons">
        <button class="caption" aria-label="最小化" title="最小化" onClick={() => bridge.notify({ type: "window.minimize" })}>
          <Icon name="minimize" />
        </button>
        <button class="caption close" aria-label="关闭" title="关闭" onClick={() => (onClose ? onClose() : bridge.notify({ type: "window.close" }))}>
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
      <span class="switch-text">{checked ? "开" : "关"}</span>
      <span class="switch-track">
        <span class="switch-thumb" />
      </span>
    </button>
  );
}

/** 标题旁的圆形问号。悬停或键盘聚焦时显示说明，避免被滚动区域裁切。 */
export function Hint({ text }: { text: string }) {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const bubbleRef = useRef<HTMLSpanElement>(null);

  useLayoutEffect(() => {
    const button = buttonRef.current;
    const bubble = bubbleRef.current;
    if (!open || !button || !bubble) return;
    const anchor = button.getBoundingClientRect();
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
    const close = () => setOpen(false);
    window.addEventListener("scroll", close, true);
    window.addEventListener("blur", close);
    return () => {
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("blur", close);
    };
  }, [open]);

  return (
    <span class="hint">
      <button
        type="button"
        class="hint-button"
        ref={buttonRef}
        aria-label={`说明。${text}`}
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
      <span class={`presence-dot tone-${tone}`} />
    </span>
  );
}

/** 点击后弹出的菜单，用于标题栏的房间切换。 */
export function Flyout({ label, button, children }: { label: string; button: ComponentChildren; children: (close: () => void) => ComponentChildren }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!root.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    const onBlur = () => setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    window.addEventListener("blur", onBlur);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("blur", onBlur);
    };
  }, [open]);

  return (
    <div class="flyout-root" ref={root}>
      <button class="flyout-button" aria-haspopup="menu" aria-expanded={open} aria-label={label} onClick={() => setOpen(!open)}>
        {button}
        <Icon name="chevronDown" class="chevron" />
      </button>
      {open && (
        <div class="flyout" role="menu">
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  );
}
