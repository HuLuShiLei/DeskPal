// DeskPal browser extension.
//
// Tells the DeskPal app on this computer the host name of the active tab and whether this browser has focus, so the
// Linux plugin can classify browsing by site. Nothing else leaves the browser: no address, no title, no page content,
// nothing from private windows, and nothing goes anywhere but 127.0.0.1.
// Works in Chromium-based browsers (Chrome, Edge, Brave, Vivaldi, Opera) and in Firefox 121 or later.
// The other side is BrowserBridge.cs in the DeskPal Linux client; keep the two in step.

const api = globalThis.browser ?? globalThis.chrome;
const ENDPOINT = "http://127.0.0.1:24913/v1/tab";

// One id per run of this background script. DeskPal forgets ids it has not heard from for a few minutes.
const INSTANCE = crypto.randomUUID();

let browserName = null;
let lastBody = null;

async function detectBrowser() {
  if (browserName) {
    return browserName;
  }

  if (typeof api.runtime.getBrowserInfo === "function") {
    browserName = "firefox";
  } else if (navigator.brave && (await navigator.brave.isBrave?.())) {
    browserName = "brave";
  } else {
    const brands = navigator.userAgentData?.brands?.map((b) => b.brand) ?? [];
    const agent = navigator.userAgent;
    if (brands.includes("Microsoft Edge") || /\bEdg\//.test(agent)) {
      browserName = "edge";
    } else if (brands.includes("Opera") || /\bOPR\//.test(agent)) {
      browserName = "opera";
    } else if (/\bVivaldi\b/.test(agent)) {
      browserName = "vivaldi";
    } else {
      browserName = "chrome";
    }
  }

  return browserName;
}

function hostOf(url) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.hostname : null;
  } catch {
    return null;
  }
}

// The active tab of the window used last, and whether that window has focus right now.
async function readState() {
  let win = null;
  try {
    win = await api.windows.getLastFocused({ populate: true, windowTypes: ["normal"] });
  } catch {
    win = null;
  }

  if (!win || win.incognito) {
    return { focused: false, domain: null };
  }

  const tab = win.tabs?.find((t) => t.active);
  return { focused: Boolean(win.focused), domain: tab ? hostOf(tab.url ?? "") : null };
}

async function setStatus(connected) {
  try {
    await api.action.setBadgeText({ text: connected ? "" : "!" });
    await api.action.setBadgeBackgroundColor({ color: "#d97706" });
    await api.action.setTitle({ title: api.i18n.getMessage(connected ? "connected" : "disconnected") });
  } catch {
    // The toolbar button is optional.
  }
}

async function report(force) {
  const state = await readState();
  const body = JSON.stringify({
    instance: INSTANCE,
    browser: await detectBrowser(),
    focused: state.focused,
    domain: state.domain,
  });
  if (!force && body === lastBody) {
    return;
  }

  try {
    const response = await fetch(ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-DeskPal": "1" },
      body,
      cache: "no-store",
      credentials: "omit",
    });
    lastBody = response.ok ? body : null;
    await setStatus(response.ok);
  } catch {
    // DeskPal is not running, or its Linux plugin is off. Try again with the next event or heartbeat.
    lastBody = null;
    await setStatus(false);
  }
}

function start() {
  // A heartbeat keeps DeskPal from forgetting this browser while nothing changes.
  api.alarms.create("deskpal-heartbeat", { periodInMinutes: 0.5 });
  report(true);
}

api.runtime.onInstalled.addListener(start);
api.runtime.onStartup.addListener(start);
api.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "deskpal-heartbeat") {
    report(true);
  }
});
api.tabs.onActivated.addListener(() => report(false));
api.tabs.onUpdated.addListener((_id, change, tab) => {
  if (tab.active && (change.url !== undefined || change.status === "complete")) {
    report(false);
  }
});
api.windows.onFocusChanged.addListener(() => report(false));

// The script also starts on its own after being idle; say hello then too.
report(false);
