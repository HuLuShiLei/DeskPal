import { useEffect, useState } from "preact/hooks";
import type { Bridge, CardMessage } from "../bridge";
import { StatusBadge } from "../host";
import { applyTheme } from "../theme";
import { Avatar, Icon } from "../ui";

/** 置顶到桌面的单人小卡片。整张卡片可以拖动，双击打开主面板（由窗口处理），关闭即取消置顶。 */
export function CardPage({ bridge }: { bridge: Bridge }) {
  const [card, setCard] = useState<CardMessage | null>(null);

  useEffect(() => {
    bridge.on((m) => {
      if (m.type !== "card") return;
      applyTheme(m.theme);
      setCard(m);
    });
    bridge.notify({ type: "ready" });
  }, []);

  if (!card) return null;
  const { member, detail } = card;
  return (
    <div class={member.online ? "pin-card" : "pin-card offline"} aria-label={member.name}>
      <Avatar name={member.name} tone={member.view.tone} />
      <div class="row-main">
        <p class="row-title">
          <span class="row-name">{member.name}</span>
          {member.meta && <span class="row-meta">{member.meta}</span>}
        </p>
        <div>
          <StatusBadge view={member.view} />
        </div>
        {detail && <p class="pin-card-detail">{detail}</p>}
      </div>
      <button class="icon-button pin-card-close" aria-label="取消置顶" title="取消置顶" onClick={() => bridge.notify({ type: "window.close" })}>
        <Icon name="close" />
      </button>
    </div>
  );
}