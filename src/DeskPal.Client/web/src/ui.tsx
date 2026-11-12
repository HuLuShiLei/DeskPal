import type { ComponentChildren } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
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
export function TitleBar({ bridge, title, children }: { bridge: Bridge; title: string; children?: ComponentChildren }) {
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
        <button class="caption close" aria-label="关闭" title="关闭" onClick={() => bridge.notify({ type: "window.close" })}>
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

/** 设置页的一行：左边是名称和说明，右边是控件。 */
export function SettingRow({ icon, title, description, children }: { icon?: IconName; title: string; description?: ComponentChildren; children?: ComponentChildren }) {
  return (
    <div class="setting-row">
      {icon && <Icon name={icon} class="setting-icon" />}
      <div class="setting-text">
        <p class="setting-title">{title}</p>
        {description && <p class="setting-desc">{description}</p>}
      </div>
      {children && <div class="setting-control">{children}</div>}
    </div>
  );
}

export function Section({ title, aside, children, label }: { title?: string; aside?: ComponentChildren; children: ComponentChildren; label?: string }) {
  return (
    <section class="section" aria-label={label ?? title}>
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
    <span class="avatar" aria-hidden="true">
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
