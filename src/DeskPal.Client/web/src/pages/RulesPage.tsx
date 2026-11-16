import { useState } from "preact/hooks";
import type { MyRuleItem, Option, PanelState, RuleKind } from "../bridge";
import { useHost } from "../host";
import { t } from "../i18n";
import { categoryTone, Hint, Icon, Section, type IconName } from "../ui";

const kindIcons: Record<RuleKind, IconName> = { Process: "app", Domain: "globe", TitleKeyword: "text" };

export function RulesPage({ state }: { state: PanelState }) {
  return (
    <>
      {state.unclassified.length > 0 && <Unclassified state={state} />}
      <MyRules state={state} />
    </>
  );
}

function KindTile({ kind, category }: { kind: RuleKind; category?: string }) {
  return (
    <span class={category ? `kind-tile tv-${categoryTone(category)}` : "kind-tile"} aria-hidden="true">
      <Icon name={kindIcons[kind]} />
    </span>
  );
}

function CategorySelect({ label, value, options, placeholder, onChange }: { label: string; value: string; options: Option<string>[]; placeholder?: string; onChange: (category: string) => void }) {
  return (
    <select class="category-select" aria-label={label} value={value} onChange={(e) => onChange(e.currentTarget.value)}>
      {placeholder && (
        <option value="" disabled>
          {placeholder}
        </option>
      )}
      {options.map((o) => (
        <option key={o.value} value={o.value}>
          {o.name}
        </option>
      ))}
    </select>
  );
}

function Unclassified({ state }: { state: PanelState }) {
  const { run } = useHost();
  return (
    <Section
      title={
        <>
          {t("unclassified.title")}
          <Hint text={t("unclassified.note")} />
        </>
      }
      label={t("unclassified.title")}
      aside={<span class="section-note">{t("unclassified.count", { count: state.unclassified.length })}</span>}
    >
      <ul class="card flush list-card">
        {state.unclassified.map((item) => (
          <li key={item.kind + item.pattern} class="card-row">
            <KindTile kind={item.kind} />
            <div class="row-main">
              <p class="row-title">
                <span class="row-name" title={item.name}>
                  {item.name}
                </span>
              </p>
              <p class="sub">{item.today}</p>
            </div>
            <CategorySelect
              label={t("unclassified.classifyName", { name: item.name })}
              value=""
              placeholder={t("unclassified.classify")}
              options={state.correction.options}
              onChange={(category) => void run({ type: "rules.set", kind: item.kind, pattern: item.pattern, name: item.name, category }, t("common.saved"))}
            />
          </li>
        ))}
      </ul>
    </Section>
  );
}

function MyRules({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<string | null>(null);

  const counts = new Map<string, number>();
  for (const r of state.myRules) counts.set(r.category, (counts.get(r.category) ?? 0) + 1);
  const used = state.correction.options.filter((o) => counts.has(o.value));
  const active = filter && counts.has(filter) ? filter : null;
  const keyword = query.trim().toLowerCase();
  const rules = state.myRules.filter(
    (r) => (!active || r.category === active) && (!keyword || [r.name, r.pattern, r.categoryName, r.kindName].some((text) => text.toLowerCase().includes(keyword))),
  );

  const change = async (rule: MyRuleItem, category: string) => {
    if (category !== rule.category) await run({ type: "rules.set", kind: rule.kind, pattern: rule.pattern, name: rule.name, category }, t("common.saved"));
  };
  const remove = async (rule: MyRuleItem) => {
    const a = await ask({ title: t("rules.deleteTitle"), message: t("rule.removeMessage", { name: rule.name }), okText: t("common.delete"), danger: true });
    if (a.ok) await run({ type: "rules.delete", kind: rule.kind, pattern: rule.pattern }, t("rules.deleted"));
  };

  return (
    <Section
      title={
        <>
          {t("rules.title")}
          <Hint text={t("rules.hint")} />
        </>
      }
      label={t("rules.title")}
      aside={state.myRules.length > 0 && <span class="section-note">{t("rules.count", { count: state.myRules.length })}</span>}
    >
      {state.myRules.length === 0 ? (
        <p class="infobar">
          <Icon name="info" />
          {t("rules.empty")}
        </p>
      ) : (
        <>
          <div class="search-bar">
            <Icon name="search" class="search-icon" />
            <input type="search" aria-label={t("rules.search")} placeholder={t("rules.searchPlaceholder")} value={query} onInput={(e) => setQuery(e.currentTarget.value)} />
          </div>
          {used.length > 1 && (
            <div class="chips" role="group" aria-label={t("rules.filter")}>
              <button class="chip" aria-pressed={!active} onClick={() => setFilter(null)}>
                {t("rules.all")}
                <span class="chip-count">{state.myRules.length}</span>
              </button>
              {used.map((o) => (
                <button key={o.value} class={`chip tv-${categoryTone(o.value)}`} aria-pressed={active === o.value} onClick={() => setFilter(active === o.value ? null : o.value)}>
                  <span class="status-dot" aria-hidden="true" />
                  {o.name}
                  <span class="chip-count">{counts.get(o.value)}</span>
                </button>
              ))}
            </div>
          )}
          {rules.length === 0 ? (
            <p class="card-note">{t("rules.notFound", { query: query.trim() })}</p>
          ) : (
            <ul class="card flush list-card">
              {rules.map((r) => (
                <li key={r.kind + r.pattern} class="card-row">
                  <KindTile kind={r.kind} category={r.category} />
                  <div class="row-main">
                    <p class="row-title">
                      <span class="row-name" title={r.name}>
                        {r.name}
                      </span>
                    </p>
                    <p class="sub" title={r.pattern}>
                      {r.kindName} · {r.pattern}
                    </p>
                  </div>
                  <CategorySelect label={t("rules.editName", { name: r.name })} value={r.category} options={state.correction.options} onChange={(category) => void change(r, category)} />
                  <button class="icon-button" title={t("common.delete")} aria-label={t("rules.deleteName", { name: r.name })} onClick={() => remove(r)}>
                    <Icon name="delete" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </Section>
  );
}