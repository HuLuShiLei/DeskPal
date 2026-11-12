using DeskPal.Client.Core.Classification;
using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// The account's personal rules. Saved on the server so they apply in every room and on every computer;
/// a local copy keeps classification working before the server answers.
/// </summary>
public sealed class PersonalRules(
    ApiClient api,
    AppSettings settings,
    PersonalRuleStore store,
    LayeredClassifier classifier,
    UnclassifiedUsage unclassified,
    ILogger<PersonalRules> logger)
{
    private IReadOnlyList<UserRuleDto> _rules = [];

    /// <summary>Raised from any thread after the rules changed.</summary>
    public event Action? Changed;

    public IReadOnlyList<UserRuleDto> Rules => _rules;

    public void LoadCached() => Apply(store.Load(settings.UserId), save: false);

    public async Task RefreshAsync()
    {
        try
        {
            Apply(await api.GetMyRulesAsync(), save: true);
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Loading personal rules failed ({Status})", ex.Status);
        }
    }

    public async Task SetAsync(RuleKind kind, string pattern, ActivityCategory category, string? name)
    {
        var saved = await api.UpsertMyRuleAsync(new UpsertUserRuleRequest(kind, pattern, category, name));
        unclassified.Forget(saved.Pattern);
        Apply(_rules.Where(r => r.Kind != saved.Kind || r.Pattern != saved.Pattern).Prepend(saved).ToList(), save: true);
    }

    public async Task DeleteAsync(RuleKind kind, string pattern)
    {
        await api.DeleteMyRuleAsync(kind, pattern);
        var normalized = RulePattern.Normalize(kind, pattern);
        Apply(_rules.Where(r => r.Kind != kind || r.Pattern != normalized).ToList(), save: true);
    }

    public void Handle(Envelope envelope)
    {
        if (envelope.Type == MessageTypes.PersonalRulesChanged)
        {
            _ = RefreshAsync();
        }
    }

    private void Apply(IReadOnlyList<UserRuleDto> rules, bool save)
    {
        _rules = rules;
        classifier.SetPersonal(PersonalRuleStore.ToRuleSet(rules));
        if (save)
        {
            store.Save(settings.UserId, rules);
        }

        Changed?.Invoke();
    }
}
