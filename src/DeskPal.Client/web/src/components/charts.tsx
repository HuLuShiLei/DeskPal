import { useRef, useState } from "preact/hooks";
import type { StatsBar, StatsRhythm, StatsView } from "../bridge";
import { t } from "../i18n";

/** 连续的下标合成一段：[起点, 个数]。 */
function runs(indexes: Iterable<number>): [number, number][] {
  const result: [number, number][] = [];
  for (const i of [...indexes].sort((a, b) => a - b)) {
    const last = result[result.length - 1];
    if (last && last[0] + last[1] === i) last[1]++;
    else result.push([i, 1]);
  }
  return result;
}

/** 一天 24 小时的横向色带。悬停显示起止时间和时长。选了时段时，其余小时变淡。 */
export function Timeline({ spans, selected = [] }: { spans: StatsView["timeline"]; selected?: number[] }) {
  const picked = new Set(selected);
  const faded = picked.size === 0 ? [] : runs(Array.from({ length: 24 }, (_, h) => h).filter((h) => !picked.has(h)));
  return (
    <div class="stats-timeline">
      <svg viewBox="0 0 86400 10" preserveAspectRatio="none" role="img" aria-label={t("charts.timeline")}>
        <rect x="0" y="0" width="86400" height="10" class="stats-track" />
        {spans.map((s) => (
          <rect key={s.start} x={s.start} y="0" width={Math.max(s.end - s.start, 60)} height="10" style={{ fill: `var(--tone-${s.tone})` }}>
            <title>{s.tip}</title>
          </rect>
        ))}
        {faded.map(([from, count]) => (
          <rect key={from} x={from * 3600} y="0" width={count * 3600} height="10" class="stats-faded" />
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

/** 拖动中的选择：起止柱和是否按住 Ctrl 追加。 */
type Drag = { from: number; to: number; add: boolean };

/**
 * 堆叠柱。每根柱按图例分色；给了 onPick 时，点有日期的柱进入那一天。
 * 给了 onSelect 时可以选柱：点一根或拖过几根选中这一段，再点唯一选中的那根取消；按住 Ctrl 追加，拖过的都已选中时改为去掉。
 */
export function Bars({ bars, max, maxLabel, legend, onPick, label = t("charts.bars"), selected = [], onSelect }: {
  bars: StatsBar[];
  max: number;
  maxLabel: string;
  legend: StatsView["legend"];
  onPick?: (date: string) => void;
  label?: string;
  selected?: number[];
  onSelect?: (indexes: number[]) => void;
}) {
  const tone = new Map(legend.map((l) => [l.key, l]));
  const width = 10;
  const svg = useRef<SVGSVGElement>(null);
  // 快速点击时 pointerup 会在重新渲染前到达，处理函数要读 ref 里的最新值。
  const dragging = useRef<Drag | null>(null);
  const [drag, setDragState] = useState<Drag | null>(null);
  const setDrag = (d: Drag | null) => {
    dragging.current = d;
    setDragState(d);
  };
  const picked = new Set(selected);

  const indexAt = (clientX: number) => {
    const box = svg.current!.getBoundingClientRect();
    return Math.min(bars.length - 1, Math.max(0, Math.floor(((clientX - box.left) / box.width) * bars.length)));
  };
  const result = (d: Drag) => {
    const span = Array.from({ length: Math.abs(d.to - d.from) + 1 }, (_, k) => Math.min(d.from, d.to) + k);
    if (!d.add) return span;
    const next = new Set(picked);
    const remove = span.every((i) => picked.has(i));
    span.forEach((i) => (remove ? next.delete(i) : next.add(i)));
    return [...next];
  };
  const shown = drag ? new Set(result(drag)) : picked;

  const select = onSelect && {
    onPointerDown: (e: PointerEvent) => {
      if (e.button !== 0) return;
      svg.current!.setPointerCapture(e.pointerId);
      const i = indexAt(e.clientX);
      setDrag({ from: i, to: i, add: e.ctrlKey || e.metaKey });
    },
    onPointerMove: (e: PointerEvent) => {
      const d = dragging.current;
      if (!d) return;
      const i = indexAt(e.clientX);
      if (i !== d.to) setDrag({ ...d, to: i });
    },
    onPointerUp: () => {
      const d = dragging.current;
      if (!d) return;
      const again = !d.add && d.from === d.to && picked.size === 1 && picked.has(d.from);
      setDrag(null);
      onSelect(again ? [] : result(d).sort((a, b) => a - b));
    },
    onPointerCancel: () => setDrag(null),
  };

  return (
    <div class={onSelect ? "stats-bars selectable" : "stats-bars"}>
      <span class="stats-max">{maxLabel}</span>
      <svg ref={svg} viewBox={`0 0 ${bars.length * width} 100`} preserveAspectRatio="none" role="img" aria-label={label} {...select}>
        {runs(shown).map(([from, count]) => (
          <g key={`pick-${from}`} class="stats-pick">
            <rect x={from * width} y="0" width={count * width} height="100" />
            <rect x={from * width + 1} y="101.5" width={count * width - 2} height="2.5" class="stats-pick-line" />
          </g>
        ))}
        <line x1="0" x2={bars.length * width} y1="0.5" y2="0.5" class="stats-grid" />
        <line x1="0" x2={bars.length * width} y1="50" y2="50" class="stats-grid" />
        {bars.map((bar, i) => {
          let y = 100;
          const clickable = bar.date !== null && onPick !== undefined;
          const classes = ["stats-bar", clickable && "clickable", shown.size > 0 && !shown.has(i) && "faded"].filter(Boolean).join(" ");
          return (
            <g key={i} class={classes} onClick={() => clickable && onPick(bar.date!)}>
              <title>{bar.tip}</title>
              <rect x={i * width} y="0" width={width} height="100" class="stats-hit" />
              {bar.parts.map((p) => {
                const h = Math.min(y, (p.seconds / max) * 100);
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
      <div class="stats-axis" style={{ gridTemplateColumns: `repeat(${bars.length}, minmax(0, 1fr))` }} aria-hidden="true">
        {bars.map((bar, i) => (
          <span key={i}>{bar.label}</span>
        ))}
      </div>
    </div>
  );
}

/** 一周节律：星期 × 小时，颜色越深用得越多。悬停显示星期、时段和时长。 */
export function Heatmap({ rhythm }: { rhythm: StatsRhythm }) {
  const color = rhythm.tone ? `var(--tone-${rhythm.tone})` : "var(--accent)";
  return (
    <div class="stats-heatmap" role="img" aria-label={t("stats.rhythm")}>
      {rhythm.rows.map((row) => (
        <div key={row.label} class="stats-heatmap-row">
          <span class="stats-heatmap-label">{row.label}</span>
          {row.cells.map((cell, hour) => (
            <span
              key={hour}
              class="stats-heatmap-cell"
              title={cell.tip}
              style={cell.seconds > 0 && rhythm.max > 0 ? { background: color, opacity: 0.15 + 0.85 * (cell.seconds / rhythm.max) } : undefined}
            />
          ))}
        </div>
      ))}
      <div class="stats-heatmap-row stats-heatmap-axis" aria-hidden="true">
        <span class="stats-heatmap-label" />
        {Array.from({ length: 24 }, (_, hour) => (
          <span key={hour}>{hour % 6 === 0 ? hour : ""}</span>
        ))}
      </div>
    </div>
  );
}

/** 图例。点某一类只看这一类，再点一次显示全部。 */
export function Legend({ legend, selected, onPick }: { legend: StatsView["legend"]; selected: string | null; onPick: (key: string | null) => void }) {
  return (
    <ul class={selected ? "stats-legend filtered" : "stats-legend"}>
      {legend.map((l) => (
        <li key={l.key} class={l.hidden ? "hidden-part" : undefined}>
          <button
            aria-pressed={selected === l.key}
            title={selected === l.key ? t("stats.filterClear") : t("stats.filterHint")}
            onClick={() => onPick(selected === l.key ? null : l.key)}
          >
            <span class="stats-swatch" style={{ background: `var(--tone-${l.tone})` }} />
            {l.name}
          </button>
        </li>
      ))}
    </ul>
  );
}

/** 排行：名称、所属应用或类别、横向占比条和时长。带 detail 的行点击后打开详情，带 filter 的类别行点击后只看这一类。 */
export function Ranking({ items, onPick }: { items: StatsView["ranking"]; onPick?: (item: StatsView["ranking"][number]) => void }) {
  return (
    <ol class="stats-ranking">
      {items.map((item, i) => {
        const pick = (item.detail || item.filter) && onPick ? () => onPick(item) : undefined;
        const tip = [item.name, item.sub, pick && t(item.filter ? "stats.filterHint" : "stats.clickForDetail")].filter(Boolean).join("\n");
        return (
        <li
          key={i}
          title={tip}
          class={pick ? "pickable" : undefined}
          role={pick ? "button" : undefined}
          tabIndex={pick ? 0 : undefined}
          onClick={pick}
          onKeyDown={pick && ((e) => (e.key === "Enter" || e.key === " ") && (e.preventDefault(), pick()))}
        >
          <div class="stats-rank-text">
            <span class="stats-rank-name">{item.name}</span>
            {item.sub && <span class="stats-rank-sub">{item.sub}</span>}
          </div>
          <span class="stats-rank-time">{item.time}</span>
          <span class="stats-rank-bar">
            <span class={item.hidden ? "hidden-part" : undefined} style={{ width: `${Math.max(item.share * 100, 1)}%`, background: `var(--tone-${item.tone})` }} />
          </span>
        </li>
        );
      })}
    </ol>
  );
}