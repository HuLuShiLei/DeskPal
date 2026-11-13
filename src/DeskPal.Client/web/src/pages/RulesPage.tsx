import { useState } from "preact/hooks";
import type { MyRuleItem, PanelState } from "../bridge";
import { useHost } from "../host";
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
      title: `「${rule.name}」算什么？`,
      select: { options: state.correction.options, initial: rule.category },
      okText: "保存",
    });
    if (a.ok && a.selected !== rule.category) {
      await run({ type: "rules.set", kind: rule.kind, pattern: rule.pattern, name: rule.name, category: a.selected }, "已保存");
    }
  };
  const remove = async (rule: MyRuleItem) => {
    const a = await ask({ title: "删除分类", message: `删除你对「${rule.name}」设置的分类，改用默认分类。`, okText: "删除", danger: true });
    if (a.ok) await run({ type: "rules.delete", kind: rule.kind, pattern: rule.pattern }, "已删除");
  };

  return (
    <Section
      title={
        <>
          我的分类
          <Hint text={"在状态页「纠正分类」或给待归类的应用归类后出现在这里。\n所有房间和电脑通用，只影响你自己。"} />
        </>
      }
      label="我的分类"
      aside={state.myRules.length > 0 && <span class="section-note">共 {state.myRules.length} 条</span>}
    >
      {state.myRules.length === 0 ? (
        <p class="infobar">
          <Icon name="info" />
          还没有设置过。在状态页点「纠正分类」，或者给待归类的应用归类后，会出现在这里。
        </p>
      ) : (
        <>
          <div class="search-bar">
            <Icon name="search" class="search-icon" />
            <input type="search" aria-label="搜索我的分类" placeholder="搜索应用、网站或类别" value={query} onInput={(e) => setQuery(e.currentTarget.value)} />
          </div>
          {rules.length === 0 ? (
            <p class="card-note">没有找到「{query.trim()}」。</p>
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
                  <button class="icon-button" title="修改" aria-label={`修改${r.name}的分类`} onClick={() => edit(r)}>
                    <Icon name="edit" />
                  </button>
                  <button class="icon-button" title="删除" aria-label={`删除${r.name}的分类`} onClick={() => remove(r)}>
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
