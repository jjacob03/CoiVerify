namespace CoiVerify.Domain;

/// <summary>
/// One coverage block on the certificate (e.g. the "COMMERCIAL GENERAL LIABILITY" row
/// on an ACORD 25). Limits are kept as a flexible name/amount map rather than fixed
/// properties, since which limits apply varies by coverage type (Each Occurrence and
/// General Aggregate for GL; Combined Single Limit for Auto; Each Accident and
/// Disease limits for Workers Comp, etc.) and carriers are not fully consistent about
/// which ones they print.
/// </summary>
public sealed record CoverageLine
{
    public required CoverageType Type { get; init; }
    public string? InsurerName { get; init; }
    public string? InsurerLetter { get; init; }
    public string? PolicyNumber { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }

    /// <summary>
    /// Limit name (e.g. "EachOccurrence", "GeneralAggregate", "CombinedSingleLimit",
    /// "EachAccident", "DiseasePolicyLimit") to dollar amount.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> Limits { get; init; } =
        new Dictionary<string, decimal>();

    public bool IsExpired(DateOnly asOf) => ExpirationDate is { } exp && exp < asOf;
}
