namespace CoiVerify.Domain;

public enum ComparisonOperator
{
    GreaterThanOrEqual,
    LessThanOrEqual,
    Equal,
    NotEqual,
    /// <summary>Date field must be on/after the given value (relative to "today" unless a literal date is given).</summary>
    OnOrAfter
}

/// <summary>
/// One requirement a caller wants checked, e.g. "GeneralLiability.EachOccurrence >= 1000000".
/// Field paths are "&lt;CoverageType&gt;.&lt;LimitName&gt;" for limits (e.g.
/// "GeneralLiability.EachOccurrence"), or one of the top-level flags/dates:
/// "AdditionalInsured", "WaiverOfSubrogation", "ExpirationDate" (checked against the
/// earliest expiration across all coverages present).
/// </summary>
public sealed record RequirementRule
{
    public required string Field { get; init; }
    public required ComparisonOperator Operator { get; init; }

    /// <summary>Stringified comparison value - "1000000", "true", "today" - parsed based on the field's type.</summary>
    public required string Value { get; init; }

    public string? Description { get; init; }
}

public sealed record ValidationRequest
{
    public required IReadOnlyList<RequirementRule> Rules { get; init; }
}

public sealed record RuleResult
{
    public required RequirementRule Rule { get; init; }
    public required bool Passed { get; init; }
    public string? ActualValue { get; init; }
    public string? Message { get; init; }
}

public sealed record ValidationResult
{
    public required bool IsCompliant { get; init; }
    public required IReadOnlyList<RuleResult> RuleResults { get; init; }
}

public interface IRulesEvaluator
{
    ValidationResult Evaluate(CertificateOfInsurance document, ValidationRequest request, DateOnly asOf);
}
