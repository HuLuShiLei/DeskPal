namespace DeskPal.Server.Features.Rules;

public sealed class RuleOptions
{
    /// <summary>Distinct accounts needed before a rule or an unknown app becomes a candidate.</summary>
    public int MinUsers { get; set; } = 5;

    /// <summary>Share of those accounts that must agree on one category.</summary>
    public double MinAgreement { get; set; } = 0.8;

    /// <summary>Accounts younger than this are not counted, so a burst of new accounts cannot push a rule.</summary>
    public int MinAccountAgeDays { get; set; } = 3;

    /// <summary>Header value required by GET /api/rules/candidates. Empty disables the endpoint. Set from Rules__MaintainerToken.</summary>
    public string MaintainerToken { get; set; } = "";
}
