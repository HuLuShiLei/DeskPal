import type { Bridge } from "./bridge";

export type Platform = "windows" | "mac";

let current: Platform = "windows";

/** 来自页面地址的 platform 参数（macOS 宿主加 platform=mac）。写到 <html data-platform>，样式和图标按它区分。 */
export function setPlatform(value: string | null | undefined) {
  current = value === "mac" ? "mac" : "windows";
  document.documentElement.dataset.platform = current;
}

export function isMac() {
  return current === "mac";
}

/** 可以拖动窗口的区域，和 styles.css 里 app-region: drag 的元素一致；其中的按钮和菜单不参与拖动。 */
const dragRegion = ".titlebar, .pin-card";
const noDrag = ".caption-buttons, .flyout-root, button, input, select, textarea, a, label, [role=button], [role=switch]";

/**
 * macOS 的 WKWebView 不支持 CSS app-region。按下标题栏空白处时通知窗口，窗口跟随鼠标移动到松开为止；
 * 双击置顶小卡片打开面板（Windows 上由窗口的非客户区双击处理）。Windows 上什么都不做。
 */
export function installWindowDrag(bridge: Bridge) {
  if (!isMac()) return;
  const draggable = (target: EventTarget | null) =>
    target instanceof Element && target.closest(dragRegion) !== null && target.closest(noDrag) === null;
  document.addEventListener("mousedown", (e) => {
    if (e.button === 0 && e.detail === 1 && draggable(e.target)) bridge.notify({ type: "window.drag" });
  });
  document.addEventListener("dblclick", (e) => {
    if (draggable(e.target)) bridge.notify({ type: "window.dblclick" });
  });
}
