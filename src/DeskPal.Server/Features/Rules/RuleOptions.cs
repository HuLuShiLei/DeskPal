namespace DeskPal.Server.Features.Rules;

public sealed class RuleOptions
{
    /// <summary>Distinct accounts needed before personal rules or a guessed app are written into the builtin rules.</summary>
    public int MinUsers { get; set; } = 2;

    /// <summary>Share of those accounts that must agree on one category.</summary>
    public double MinAgreement { get; set; } = 0.66;

    /// <summary>
    /// Distinct accounts needed before an app is listed for a person to decide (unknown apps, personal rules that do not
    /// agree enough yet). The list ends up in a pull request.
    /// </summary>
    public int ReviewMinUsers { get; set; } = 1;

    /// <summary>Accounts younger than this are not counted, so a burst of new accounts cannot push a rule.</summary>
    public int MinAccountAgeDays { get; set; } = 3;

    /// <summary>Header value required by GET /api/rules/candidates. Empty disables the endpoint. Set from Rules__MaintainerToken.</summary>
    public string MaintainerToken { get; set; } = "";
}
