import { createContext } from "preact";
import { useContext, useEffect, useRef, useState } from "preact/hooks";
import type { Bridge, Command, CommandResult, PresenceView } from "./bridge";

export interface DialogOptions {
  title: string;
  message?: string;
  /** 醒目显示的一段文字，例如邀请码。 */
  highlight?: string;
  input?: { initial?: string; placeholder?: string; maxLength?: number; required?: boolean };
  select?: { options: { value: string; name: string }[]; initial?: string };
  checkbox?: { label: string; initial: boolean; disabled?: boolean };
  okText?: string;
  /** null 表示只有一个按钮。 */
  cancelText?: string | null;
  danger?: boolean;
}

export interface DialogAnswer {
  ok: boolean;
  text: string;
  selected: string;
  checked: boolean;
}

interface Toast {
  id: number;
  text: string;
  error: boolean;
}

export interface Host {
  bridge: Bridge;
  ask(options: DialogOptions): Promise<DialogAnswer>;
  toast(text: string, error?: boolean): void;
  /** 发命令；失败时显示宿主返回的错误。 */
  run(command: Command, success?: string): Promise<CommandResult>;
}

export const HostContext = createContext<Host | null>(null);

export const useHost = () => useContext(HostContext)!;

interface PendingDialog {
  options: DialogOptions;
  resolve: (answer: DialogAnswer) => void;
}

export function useHostServices(bridge: Bridge) {
  const [dialogs, setDialogs] = useState<PendingDialog[]>([]);
  const [toasts, setToasts] = useState<Toast[]>([]);

  const toast = (text: string, error = false) => {
    const id = Date.now() + Math.random();
    setToasts((list) => [...list, { id, text, error }]);
    setTimeout(() => setToasts((list) => list.filter((t) => t.id !== id)), error ? 6000 : 3000);
  };

  const ask = (options: DialogOptions) =>
    new Promise<DialogAnswer>((resolve) => setDialogs((list) => [...list, { options, resolve }]));

  const run = async (command: Command, success?: string) => {
    const result = await bridge.request(command);
    if (!result.ok) toast(result.error ?? "操作失败", true);
    else if (success) toast(success);
    return result;
  };

  const close = (answer: DialogAnswer) => {
    setDialogs(([first, ...rest]) => {
      first?.resolve(answer);
      return rest;
    });
  };

  const host: Host = { bridge, ask, toast, run };
  const overlay = (
    <>
      {dialogs[0] && <Dialog key={dialogs[0].options.title + dialogs.length} options={dialogs[0].options} onClose={close} />}
      <div class="toasts" role="status">
        {toasts.map((t) => (
          <div key={t.id} class={t.error ? "toast error" : "toast"}>
            <span class="icon" aria-hidden="true">
              {t.error ? "\uE7BA" : "\uE8FB"}
            </span>
            {t.text}
          </div>
        ))}
      </div>
    </>
  );
  return { host, overlay, dialogOpen: dialogs.length > 0 };
}

function Dialog({ options, onClose }: { options: DialogOptions; onClose: (a: DialogAnswer) => void }) {
  const [text, setText] = useState(options.input?.initial ?? "");
  const [selected, setSelected] = useState(options.select?.initial ?? options.select?.options[0]?.value ?? "");
  const [checked, setChecked] = useState(options.checkbox?.initial ?? false);
  const answer = (ok: boolean) => onClose({ ok, text: text.trim(), selected, checked });
  const blocked = options.input?.required && text.trim().length === 0;
  const form = useRef<HTMLFormElement>(null);

  useEffect(() => {
    const fallback = options.danger ? "button[type=button]" : "button[type=submit]";
    form.current?.querySelector<HTMLElement>(`input:not([type=checkbox]), select, ${fallback}`)?.focus();
  }, []);

  return (
    <div class="backdrop">
      <form
        ref={form}
        class="dialog"
        role="dialog"
        aria-modal="true"
        aria-label={options.title}
        onSubmit={(e) => {
          e.preventDefault();
          if (!blocked) answer(true);
        }}
        onKeyDown={(e) => {
          if (e.key === "Escape" && options.cancelText !== null) answer(false);
        }}
      >
        <div class="dialog-body">
        <h2>{options.title}</h2>
        {options.message && <p class="dialog-message">{options.message}</p>}
        {options.highlight && <p class="highlight selectable">{options.highlight}</p>}
        {options.input && (
          <input
            aria-label={options.title}
            value={text}
            placeholder={options.input.placeholder}
            maxLength={options.input.maxLength}
            onInput={(e) => setText(e.currentTarget.value)}
          />
        )}
        {options.select && (
          <select aria-label={options.title} value={selected} onChange={(e) => setSelected(e.currentTarget.value)}>
            {options.select.options.map((o) => (
              <option key={o.value} value={o.value}>
                {o.name}
              </option>
            ))}
          </select>
        )}
        {options.checkbox && (
          <label class="check">
            <input
              type="checkbox"
              checked={checked}
              disabled={options.checkbox.disabled}
              onChange={(e) => setChecked(e.currentTarget.checked)}
            />
            {options.checkbox.label}
          </label>
        )}
        </div>
        <div class="dialog-buttons">
          {options.cancelText !== null && (
            <button type="button" onClick={() => answer(false)}>
              {options.cancelText ?? "取消"}
            </button>
          )}
          <button type="submit" class={options.danger ? "primary danger" : "primary"} disabled={blocked}>
            {options.okText ?? "确定"}
          </button>
        </div>
      </form>
    </div>
  );
}

/** 带状态色的标签，例如「● 在聊天」。 */
export function StatusBadge({ view }: { view: PresenceView }) {
  return (
    <span class={`status-badge tv-${view.tone}`}>
      <span class="status-dot" aria-hidden="true" />
      {view.headline}
    </span>
  );
}

/** 应用名一行，窗口标题最多两行；完整内容悬停查看。 */
export function PresenceDetail({ view }: { view: PresenceView }) {
  if (!view.app && !view.detail) return null;
  return (
    <div class="presence-detail" title={[view.app, view.detail].filter(Boolean).join("\n")}>
      {view.app && <p class="presence-app">{view.app}</p>}
      {view.detail && <p class="presence-title">{view.detail}</p>}
    </div>
  );
}

/** 从宿主给的剩余毫秒开始倒数。每次推送都重新对时。 */
export function useCountdown(remainingMs: number) {
  const [deadline, setDeadline] = useState(() => Date.now() + remainingMs);
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    setDeadline(Date.now() + remainingMs);
    setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(timer);
  }, [remainingMs]);
  return Math.max(0, deadline - now);
}