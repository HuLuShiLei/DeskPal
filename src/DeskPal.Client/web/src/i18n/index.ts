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

/** {name} 换成 vars.name；没给的占位符原样保留。 */
export function t(key: MessageKey, vars?: Record<string, string | number>): string {
  const text: string = (current === "en" ? en : zh)[key];
  return vars ? text.replace(/\{(\w+)\}/g, (hole, name: string) => (name in vars ? String(vars[name]) : hole)) : text;
}