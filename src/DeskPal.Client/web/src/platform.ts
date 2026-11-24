import type { Bridge } from "./bridge";

export type Platform = "windows" | "mac" | "linux";

let current: Platform = "windows";

/** 来自页面地址的 platform 参数（macOS 宿主加 platform=mac，Linux 宿主加 platform=linux）。写到 <html data-platform>，样式和图标按它区分。 */
export function setPlatform(value: string | null | undefined) {
  current = value === "mac" || value === "linux" ? value : "windows";
  document.documentElement.dataset.platform = current;
}

export function isMac() {
  return current === "mac";
}

export function isLinux() {
  return current === "linux";
}

/** Windows 的页面跑在 WebView2 里，有 Segoe Fluent Icons 和 CSS app-region；macOS、Linux 是 WebKit，两样都没有。 */
export function isWindows() {
  return current === "windows";
}

/** 可以拖动窗口的区域，和 styles.css 里 app-region: drag 的元素一致；其中的按钮和菜单不参与拖动。 */
const dragRegion = ".titlebar, .pin-card";
const noDrag = ".caption-buttons, .flyout-root, button, input, select, textarea, a, label, [role=button], [role=switch]";

/**
 * macOS 的 WKWebView 和 Linux 的 WebKitGTK 不支持 CSS app-region。按下标题栏空白处时通知窗口：macOS 上窗口跟随鼠标移动到松开为止，
 * Linux 上交给窗口管理器移动（x、y 是按下的位置，窗口管理器需要）。
 * 双击置顶小卡片打开面板（Windows 上由窗口的非客户区双击处理）。Windows 上什么都不做。
 */
export function installWindowDrag(bridge: Bridge) {
  if (isWindows()) return;
  const draggable = (target: EventTarget | null) =>
    target instanceof Element && target.closest(dragRegion) !== null && target.closest(noDrag) === null;
  document.addEventListener("mousedown", (e) => {
    if (e.button === 0 && e.detail === 1 && draggable(e.target)) bridge.notify({ type: "window.drag", x: e.clientX, y: e.clientY });
  });
  document.addEventListener("dblclick", (e) => {
    if (draggable(e.target)) bridge.notify({ type: "window.dblclick" });
  });
}
