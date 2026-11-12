/** 与 index.html 里的首屏脚本保持同一规则：system 跟随 Windows，light / dark 固定。 */
export function applyTheme(theme: string | null | undefined) {
  const choice = theme === "light" || theme === "dark" ? theme : "system";
  const dark = choice === "dark" || (choice === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.dataset.theme = choice;
  document.documentElement.classList.toggle("dark", dark);
}

export function watchSystemTheme() {
  const mq = window.matchMedia("(prefers-color-scheme: dark)");
  const onChange = () => {
    if (document.documentElement.dataset.theme === "system") applyTheme("system");
  };
  mq.addEventListener("change", onChange);
  return () => mq.removeEventListener("change", onChange);
}
