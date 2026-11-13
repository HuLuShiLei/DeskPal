import { useRef, useState } from "preact/hooks";
import type { PanelState } from "../bridge";
import { useHost } from "../host";
import { sharingMessage } from "../sharing";
import { Icon, Section, SettingRow, Switch } from "../ui";

const issuePageUrl = "https://github.com/HuLuShiLei/DeskPal/issues/new/choose";

function openIssuePage() {
  const link = document.createElement("a");
  link.href = issuePageUrl;
  link.target = "_blank";
  link.rel = "noreferrer";
  link.click();
}

export function SettingsPage({ state }: { state: PanelState }) {
  const { run, ask } = useHost();
  const s = state.settings;
  const [pressed, setPressed] = useState(false);
  const seen = useRef(false);
  if (s.updateActivity) seen.current = true;
  const status = s.updateActivity ?? (pressed && !seen.current ? "正在检查更新…" : null);
  const waiting = pressed || s.updateActivity !== null;

  const check = async () => {
    seen.current = false;
    setPressed(true);
    try {
      await run({ type: "update.check" });
    } finally {
      setPressed(false);
      seen.current = false;
    }
  };

  const rename = async () => {
    const a = await ask({
      title: "修改显示名称",
      message: "房间成员看到的名称。可以和别人重名。",
      input: { initial: state.me.displayName, placeholder: "1 到 20 个字符", maxLength: 20, required: true },
      okText: "保存",
    });
    if (a.ok && a.text !== state.me.displayName) await run({ type: "me.rename", name: a.text }, "已修改");
  };

  const logout = async () => {
    const answer = await ask({
      title: "退出登录",
      message: "确定退出登录？之后会回到登录页，可以换账号或服务器。",
      okText: "退出登录",
      danger: true,
    });
    if (answer.ok) await run({ type: "auth.logout" });
  };

  const sharing = async (enabled: boolean) => {
    if (enabled) {
      const a = await ask({ title: "帮助改进默认分类", message: sharingMessage, okText: "同意分享" });
      if (!a.ok) return;
    }
    await run({ type: "settings.shareUsage", enabled }, enabled ? "已开启" : "已关闭，已上传的未识别应用记录会删除");
  };

  return (
    <>
      <Section title="账号">
        <div class="card flush">
          <SettingRow title={state.me.displayName} description={
              <>
                {s.userName}
                <span class="sub" title={s.serverUrl}>
                  {s.serverUrl}
                </span>
              </>
            }
          >
            <button class="icon-button" title="修改显示名称" aria-label="修改显示名称" onClick={rename}>
              <Icon name="edit" />
            </button>
            <button class="subtle danger" onClick={logout}>
              退出登录
            </button>
          </SettingRow>
        </div>
      </Section>
      <Section title="常规">
        <div class="card flush">
          <SettingRow title="开机自启" hint="登录 Windows 后在后台启动">
            <Switch checked={s.autoStart} label="开机自启" onChange={(v) => run({ type: "settings.autoStart", enabled: v })} />
          </SettingRow>
          <SettingRow title="窗口置顶" hint="面板显示在其他窗口上面">
            <Switch checked={s.topmost} label="窗口置顶" onChange={(v) => run({ type: "settings.topmost", enabled: v })} />
          </SettingRow>
          <SettingRow title="关闭窗口时" hint="点击关闭按钮后的操作">
            <select
              aria-label="关闭窗口时"
              value={s.closeAction}
              onChange={(e) => run({ type: "settings.closeAction", action: e.currentTarget.value as "ask" | "minimize" | "tray" })}
            >
              <option value="ask">每次询问</option>
              <option value="minimize">最小化</option>
              <option value="tray">最小化到系统托盘</option>
            </select>
          </SettingRow>
          <SettingRow title="夜间模式" hint="跟随 Windows，或固定为浅色、深色">
            <select aria-label="夜间模式" value={s.theme} onChange={(e) => run({ type: "settings.theme", theme: e.currentTarget.value as "system" | "light" | "dark" })}>
              <option value="system">跟随系统</option>
              <option value="light">浅色</option>
              <option value="dark">深色</option>
            </select>
          </SettingRow>
        </div>
      </Section>
      <Section title="分类">
        <div class="card flush">
          <SettingRow title="帮助改进默认分类" hint="上传没有识别的桌面应用和你的分类，不含窗口标题和网页">
            <Switch checked={s.shareUsage === true} label="帮助改进默认分类" onChange={sharing} />
          </SettingRow>
        </div>
      </Section>
      <Section title="更新">
        <div class="card flush">
          <SettingRow title={`当前版本 ${s.version}`} description={status ?? (s.updateReady ? "新版本已下载，重启后安装" : undefined)}>
            <button disabled={waiting} onClick={check}>
              {waiting ? "请稍候…" : "检查更新"}
            </button>
          </SettingRow>
          <SettingRow title="加入测试版" hint="更早收到新功能，可能不稳定">
            <Switch checked={s.receiveBeta} label="加入测试版" onChange={(v) => run({ type: "update.beta", enabled: v })} />
          </SettingRow>
          {s.updateReady && (
            <SettingRow title="新版本已就绪">
              <button class="primary" onClick={() => run({ type: "update.restart" })}>
                立即重启并更新
              </button>
            </SettingRow>
          )}
        </div>
      </Section>
      <Section title="反馈">
        <div class="card flush">
          <SettingRow title="问题与建议" hint="在浏览器中提交问题反馈或功能建议">
            <button onClick={openIssuePage}>打开</button>
          </SettingRow>
        </div>
      </Section>
    </>
  );
}
