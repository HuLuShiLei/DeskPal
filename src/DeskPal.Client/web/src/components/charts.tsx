import type { StatsView } from "../bridge";

/** 一天 24 小时的横向色带。悬停显示起止时间和时长。 */
export function Timeline({ spans }: { spans: StatsView["timeline"] }) {
  return (
    <div class="stats-timeline">
      <svg viewBox="0 0 86400 10" preserveAspectRatio="none" role="img" aria-label="一天的时间轴">
        <rect x="0" y="0" width="86400" height="10" class="stats-track" />
        {spans.map((s) => (
          <rect key={s.start} x={s.start} y="0" width={Math.max(s.end - s.start, 60)} height="10" style={{ fill: `var(--tone-${s.tone})` }}>
            <title>{s.tip}</title>
          </rect>
        ))}
      </svg>
      <div class="stats-axis five" aria-hidden="true">
        {[0, 6, 12, 18, 24].map((h) => (
          <span key={h}>{h}</span>
        ))}
      </div>
    </div>
  );
}

/** 堆叠柱。每根柱按图例分色；点有日期的柱进入那一天。 */
export function Bars({ view, onPick }: { view: StatsView; onPick: (date: string) => void }) {
  const tone = new Map(view.legend.map((l) => [l.key, l]));
  const width = 10;
  return (
    <div class="stats-bars">
      <span class="stats-max">{view.barMaxLabel}</span>
      <svg viewBox={`0 0 ${view.bars.length * width} 100`} preserveAspectRatio="none" role="img" aria-label="使用时长柱状图">
        <line x1="0" x2={view.bars.length * width} y1="0.5" y2="0.5" class="stats-grid" />
        <line x1="0" x2={view.bars.length * width} y1="50" y2="50" class="stats-grid" />
        {view.bars.map((bar, i) => {
          let y = 100;
          const clickable = bar.date !== null && view.scale !== "Day";
          return (
            <g key={i} class={clickable ? "stats-bar clickable" : "stats-bar"} onClick={() => clickable && onPick(bar.date!)}>
              <title>{bar.tip}</title>
              <rect x={i * width} y="0" width={width} height="100" class="stats-hit" />
              {bar.parts.map((p) => {
                const h = Math.min(y, (p.seconds / view.barMax) * 100);
                y -= h;
                const legend = tone.get(p.key);
                return (
                  <rect
                    key={p.key}
                    x={i * width + 1.5}
                    y={y}
                    width={width - 3}
                    height={h}
                    class={legend?.hidden ? "hidden-part" : undefined}
                    style={{ fill: `var(--tone-${legend?.tone ?? "other"})` }}
                  />
                );
              })}
            </g>
          );
        })}
      </svg>
      <div class="stats-axis" style={{ gridTemplateColumns: `repeat(${view.bars.length}, minmax(0, 1fr))` }} aria-hidden="true">
        {view.bars.map((bar, i) => (
          <span key={i}>{bar.label}</span>
        ))}
      </div>
    </div>
  );
}

export function Legend({ legend }: { legend: StatsView["legend"] }) {
  return (
    <ul class="stats-legend">
      {legend.map((l) => (
        <li key={l.key} class={l.hidden ? "hidden-part" : undefined}>
          <span class="stats-swatch" style={{ background: `var(--tone-${l.tone})` }} />
          {l.name}
        </li>
      ))}
    </ul>
  );
}

/** 排行：名称、所属应用或类别、横向占比条和时长。 */
export function Ranking({ items }: { items: StatsView["ranking"] }) {
  return (
    <ol class="stats-ranking">
      {items.map((item, i) => (
        <li key={i} title={item.sub ? `${item.name}\n${item.sub}` : item.name}>
          <div class="stats-rank-text">
            <span class="stats-rank-name">{item.name}</span>
            {item.sub && <span class="stats-rank-sub">{item.sub}</span>}
          </div>
          <span class="stats-rank-time">{item.time}</span>
          <span class="stats-rank-bar">
            <span class={item.hidden ? "hidden-part" : undefined} style={{ width: `${Math.max(item.share * 100, 1)}%`, background: `var(--tone-${item.tone})` }} />
          </span>
        </li>
      ))}
    </ol>
  );
}