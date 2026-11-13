import { useState } from "preact/hooks";
import type { MyRuleItem, PanelState } from "../bridge";
import { useHost } from "../host";
import { t } from "../i18n";
import { Hint, Icon, Section } from "../ui";

export function RulesPage({ state }: { state: PanelState }) {
  const { ask, run } = useHost();
  const [query, setQuery] = useState("");
  const keyword = query.trim().toLowerCase();
  const rules = keyword
    ? state.myRules.filter((r) => [r.name, r.pattern, r.categoryName, r.kindName].some((text) => text.toLowerCase().includes(keyword)))
    : state.myRules;

  const edit = async (rule: MyRuleItem) => {
    const a = await ask({
      title: t("rule.ask", { name: rule.name }),
      select: { options: state.correction.options, initial: rule.category },
      okText: t("common.save"),
    });
    if (a.ok && a.selected !== rule.category) {
      await run({ type: "rules.set", kind: rule.kind, pattern: rule.pattern, name: rule.name, category: a.selected }, t("common.saved"));
    }
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
          {rules.length === 0 ? (
            <p class="card-note">{t("rules.notFound", { query: query.trim() })}</p>
          ) : (
            <ul class="list">
              {rules.map((r) => (
                <li key={r.kind + r.pattern} class="list-row compact">
                  <div class="row-main">
                    <p class="row-title"><span class="row-name" title={r.name}>{r.name}</span></p>
                    <p class="sub">
                      {r.kindName} · {r.pattern}
                    </p>
                  </div>
                  <span class="pill">{r.categoryName}</span>
                  <button class="icon-button" title={t("common.edit")} aria-label={t("rules.editName", { name: r.name })} onClick={() => edit(r)}>
                    <Icon name="edit" />
                  </button>
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
