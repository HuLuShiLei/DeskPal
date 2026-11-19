import { isMac } from "../platform";
import { en } from "./en";
import { zh } from "./zh-CN";

export type Locale = "zh-CN" | "en";
export type MessageKey = keyof typeof zh;

let current: Locale = "zh-CN";

/** zh* 用中文，其他一律英文，和 C# 的 UiLanguage.Resolve 一致。 */
export function setLocale(locale: string | null | undefined) {
  current = locale === "en" ? "en" : "zh-CN";
  document.documentElement.lang = current;
}

export function getLocale(): Locale {
  return current;
}

/** macOS 上换掉提到 Windows、系统托盘的说法。只列和 Windows 不同的文字。 */
const mac: Record<Locale, Partial<Record<MessageKey, string>>> = {
  "zh-CN": {
    "settings.autoStartHint": "登录 macOS 后在后台启动",
    "settings.closeTray": "隐藏到菜单栏",
    "settings.themeHint": "跟随 macOS，或固定为浅色、深色",
    "settings.languageHint": "跟随 macOS 语言，或固定为简体中文、English",
  },
  en: {
    "settings.autoStart": "Open at login",
    "settings.autoStartHint": "Start in the background when you log in to your Mac",
    "settings.closeTray": "Hide to the menu bar",
    "settings.themeHint": "Follow macOS, or always use light or dark",
    "settings.languageHint": "Follow the macOS language, or always use 简体中文 or English",
  },
};

/** {name} 换成 vars.name；没给的占位符原样保留。 */
export function t(key: MessageKey, vars?: Record<string, string | number>): string {
  const text: string = (isMac() ? mac[current][key] : undefined) ?? (current === "en" ? en : zh)[key];
  return vars ? text.replace(/\{(\w+)\}/g, (hole, name: string) => (name in vars ? String(vars[name]) : hole)) : text;
}