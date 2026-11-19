using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Personal;
using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// The account's personal rules and no-title rules. This computer's copy is the one that counts; with account sync on,
/// changes go to the server first and the server's rules replace the copy when they change there.
/// </summary>
public sealed class PersonalRules(
    ApiClient api,
    AppSettings settings,
    SettingsStore settingsStore,
    PersonalDataStore store,
    LayeredClassifier classifier,
    UnclassifiedUsage unclassified,
    ILogger<PersonalRules> logger)
{
    /// <summary>Where older versions cached the server's rules; read once to move them into <see cref="PersonalDataStore"/>.</summary>
    private static readonly string LegacyCache = Path.Combine(SettingsStore.Directory, "personal-rules.json");

    private readonly Lock _lock = new();
    private PersonalData _data = PersonalData.Empty;
    private IReadOnlyList<NoTitleRule> _noTitle = [];

    /// <summary>Raised from any thread after the rules changed.</summary>
    public event Action? Changed;

    /// <summary>Raised after a change made on this computer, e.g. to write it to the sync folder.</summary>
    public event Action? Edited;

    public IReadOnlyList<UserRuleDto> Rules => _data.Rules;

    public IReadOnlyList<NoTitleRuleDto> NoTitleItems => _data.NoTitleRules;

    public IReadOnlyList<NoTitleRule> NoTitle => _noTitle;

    public PersonalData Data => _data;

    /// <summary>Loads this computer's copy, moving in what older versions kept elsewhere.</summary>
    public void Load()
    {
        var data = store.Load(settings.UserId);
        var moved = data is null;
        data ??= new PersonalData(new PersonalRuleStore(LegacyCache).Load(settings.UserId), [], []).Sanitize();
        if (settings.NoTitleRules.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var rule in settings.NoTitleRules)
            {
                if (NoTitleRule.Create(rule.Kind, rule.Pattern) is { } valid && data.NoTitleRules.All(r => !r.Rule.SameAs(valid)))
                {
                    data = data.WithNoTitle(new NoTitleRuleDto(valid.Kind, valid.Pattern, now));
                }
            }

            settings.NoTitleRules = [];
            settingsStore.Save(settings);
            moved = true;
        }

        Apply(data, save: moved);
    }

    /// <summary>Takes the server's rules while account sync is on.</summary>
    public async Task RefreshAsync()
    {
        if (!settings.PersonalSync)
        {
            return;
        }

        try
        {
            var rules = await api.GetMyRulesAsync();
            var noTitle = await api.GetMyNoTitleRulesAsync();
            Replace(new PersonalData(rules, noTitle, _data.Tombstones));
        }
        catch (ApiException ex) when (ex.Status == 403)
        {
            SyncTurnedOff();
            Changed?.Invoke();
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Loading personal rules failed ({Status})", ex.Status);
        }
    }

    public async Task SetAsync(RuleKind kind, string pattern, ActivityCategory category, string? name)
    {
        UserRuleDto? saved = null;
        if (settings.PersonalSync)
        {
            saved = await OnServerAsync(() => api.UpsertMyRuleAsync(new UpsertUserRuleRequest(kind, pattern, category, name)));
        }

        if (saved is null)
        {
            if (PersonalLimits.CheckRule(kind, pattern, category, out var normalized) is { } error)
            {
                throw Rejected(error);
            }

            if (_data.Rules.All(r => PersonalData.Key(r) != PersonalData.Key(kind, normalized)) && _data.Rules.Count >= PersonalLimits.MaxRules)
            {
                throw Rejected(ApiErrorCodes.RuleLimit, PersonalLimits.MaxRules);
            }

            saved = new UserRuleDto(kind, normalized, category, PersonalLimits.CleanName(name), DateTimeOffset.UtcNow);
        }

        unclassified.Forget(saved.Pattern);
        Update(d => d.WithRule(saved));
    }

    public async Task DeleteAsync(RuleKind kind, string pattern)
    {
        if (settings.PersonalSync)
        {
            await OnServerAsync(async () =>
            {
                await api.DeleteMyRuleAsync(kind, pattern);
                return string.Empty;
            });
        }

        var normalized = RulePattern.Normalize(kind, pattern);
        Update(d => d.WithoutRule(kind, normalized, DateTimeOffset.UtcNow));
    }

    public async Task AddNoTitleAsync(NoTitleRule rule)
    {
        if (_data.NoTitleRules.Any(r => r.Rule.SameAs(rule)))
        {
            return;
        }

        NoTitleRuleDto? saved = null;
        if (settings.PersonalSync)
        {
            saved = await OnServerAsync(() => api.UpsertMyNoTitleRuleAsync(new UpsertNoTitleRuleRequest(rule.Kind, rule.Pattern)));
        }

        if (saved is null && _data.NoTitleRules.Count >= PersonalLimits.MaxNoTitleRules)
        {
            throw Rejected(ApiErrorCodes.NoTitleLimit, PersonalLimits.MaxNoTitleRules);
        }

        saved ??= new NoTitleRuleDto(rule.Kind, rule.Pattern, DateTimeOffset.UtcNow);
        Update(d => d.WithNoTitle(saved));
    }

    public async Task RemoveNoTitleAsync(NoTitleRule rule)
    {
        if (settings.PersonalSync)
        {
            await OnServerAsync(async () =>
            {
                await api.DeleteMyNoTitleRuleAsync(rule.Kind, rule.Pattern);
                return string.Empty;
            });
        }

        Update(d => d.WithoutNoTitle(rule, DateTimeOffset.UtcNow));
    }

    /// <summary>Replaces this computer's copy, e.g. with what the server keeps after sync was turned on.</summary>
    /// <param name="edited">The result has changes the sync folder does not have yet.</param>
    public void Replace(PersonalData data, bool edited = false)
    {
        lock (_lock)
        {
            Apply(data.Sanitize(), save: true);
        }

        if (edited)
        {
            Edited?.Invoke();
        }
    }

    public void Handle(Envelope envelope)
    {
        if (envelope.Type == MessageTypes.PersonalRulesChanged)
        {
            _ = RefreshAsync();
        }
    }

    /// <returns>null when the account turned sync off elsewhere; the change is then saved only here.</returns>
    private async Task<T?> OnServerAsync<T>(Func<Task<T>> call)
        where T : class
    {
        try
        {
            return await call();
        }
        catch (ApiException ex) when (ex.Status == 403)
        {
            SyncTurnedOff();
            return null;
        }
    }

    private void SyncTurnedOff()
    {
        logger.LogInformation("Personal sync is off for the account; keeping the rules on this computer");
        settings.PersonalSync = false;
        settingsStore.Save(settings);
    }

    private void Update(Func<PersonalData, PersonalData> change)
    {
        lock (_lock)
        {
            Apply(change(_data), save: true);
        }

        Edited?.Invoke();
    }

    private void Apply(PersonalData data, bool save)
    {
        _data = data;
        _noTitle = data.NoTitleRules.Select(r => r.Rule).ToList();
        classifier.SetPersonal(PersonalRuleStore.ToRuleSet(data.Rules));
        if (save)
        {
            store.Save(settings.UserId, data);
        }

        Changed?.Invoke();
    }

    private static ApiException Rejected(string code, int? limit = null) => new(
        ApiErrorText.Describe(code, limit is { } l ? new Dictionary<string, string> { [ApiErrorCodes.LimitArg] = l.ToString(System.Globalization.CultureInfo.InvariantCulture) } : null) ?? code,
        400);
}