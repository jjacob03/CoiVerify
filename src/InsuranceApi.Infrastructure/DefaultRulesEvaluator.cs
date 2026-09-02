using System.Globalization;
using InsuranceApi.Domain;

namespace InsuranceApi.Infrastructure;

/// <summary>
/// Generic requirement evaluator. This is the reusable piece: every future document
/// product (loss runs, applications, etc.) can reuse this same engine against its own
/// schema, as long as fields resolve to a comparable value the way they do here.
/// </summary>
public sealed class DefaultRulesEvaluator : IRulesEvaluator
{
    public ValidationResult Evaluate(CertificateOfInsurance document, ValidationRequest request, DateOnly asOf)
    {
        var results = new List<RuleResult>(request.Rules.Count);

        foreach (var rule in request.Rules)
        {
            results.Add(EvaluateRule(document, rule, asOf));
        }

        return new ValidationResult
        {
            IsCompliant = results.All(r => r.Passed),
            RuleResults = results
        };
    }

    private static RuleResult EvaluateRule(CertificateOfInsurance document, RequirementRule rule, DateOnly asOf)
    {
        // Top-level boolean/date flags.
        switch (rule.Field)
        {
            case "AdditionalInsured":
                return EvaluateBool(rule, document.AdditionalInsured);
            case "WaiverOfSubrogation":
                return EvaluateBool(rule, document.WaiverOfSubrogation);
            case "ExpirationDate":
                return EvaluateEarliestExpiration(document, rule, asOf);
        }

        // Otherwise expect "<CoverageType>.<LimitName>", e.g. "GeneralLiability.EachOccurrence".
        var parts = rule.Field.Split('.', 2);
        if (parts.Length != 2 || !Enum.TryParse<CoverageType>(parts[0], out var coverageType))
        {
            return Fail(rule, null, $"Unrecognized field '{rule.Field}'.");
        }

        var coverage = document.GetCoverage(coverageType);
        if (coverage is null)
        {
            return Fail(rule, null, $"Certificate does not include {coverageType} coverage.");
        }

        var limitName = parts[1];
        if (!coverage.Limits.TryGetValue(limitName, out var actual))
        {
            return Fail(rule, null, $"{coverageType} coverage does not report a '{limitName}' limit.");
        }

        if (!decimal.TryParse(rule.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
        {
            return Fail(rule, actual.ToString(CultureInfo.InvariantCulture), $"Rule value '{rule.Value}' is not a valid number.");
        }

        var passed = rule.Operator switch
        {
            ComparisonOperator.GreaterThanOrEqual => actual >= expected,
            ComparisonOperator.LessThanOrEqual => actual <= expected,
            ComparisonOperator.Equal => actual == expected,
            ComparisonOperator.NotEqual => actual != expected,
            _ => throw new NotSupportedException($"Operator {rule.Operator} is not valid for a numeric limit.")
        };

        return new RuleResult
        {
            Rule = rule,
            Passed = passed,
            ActualValue = actual.ToString(CultureInfo.InvariantCulture),
            Message = passed ? null : $"{coverageType}.{limitName} is {FormatUsd(actual)}, required {rule.Operator} {FormatUsd(expected)}."
        };
    }

    private static RuleResult EvaluateEarliestExpiration(CertificateOfInsurance document, RequirementRule rule, DateOnly asOf)
    {
        var expirations = document.Coverages
            .Select(c => c.ExpirationDate)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

        if (expirations.Count == 0)
        {
            return Fail(rule, null, "Certificate does not report any expiration dates.");
        }

        var earliest = expirations.Min();

        var compareTo = rule.Value.Equals("today", StringComparison.OrdinalIgnoreCase)
            ? asOf
            : DateOnly.Parse(rule.Value, CultureInfo.InvariantCulture);

        var passed = rule.Operator switch
        {
            ComparisonOperator.OnOrAfter => earliest >= compareTo,
            _ => throw new NotSupportedException($"Operator {rule.Operator} is not valid for ExpirationDate; use OnOrAfter.")
        };

        return new RuleResult
        {
            Rule = rule,
            Passed = passed,
            ActualValue = earliest.ToString("O"),
            Message = passed ? null : $"Earliest coverage expiration is {earliest:O}, required on/after {compareTo:O}."
        };
    }

    private static RuleResult EvaluateBool(RequirementRule rule, bool actual)
    {
        var expected = bool.Parse(rule.Value);
        var passed = rule.Operator switch
        {
            ComparisonOperator.Equal => actual == expected,
            ComparisonOperator.NotEqual => actual != expected,
            _ => throw new NotSupportedException($"Operator {rule.Operator} is not valid for a boolean field; use Equal or NotEqual.")
        };

        return new RuleResult
        {
            Rule = rule,
            Passed = passed,
            ActualValue = actual.ToString(),
            Message = passed ? null : $"{rule.Field} is {actual}, required {expected}."
        };
    }

    private static RuleResult Fail(RequirementRule rule, string? actualValue, string message) => new()
    {
        Rule = rule,
        Passed = false,
        ActualValue = actualValue,
        Message = message
    };

    // ACORD 25 limits are always USD - format explicitly rather than relying on the
    // host's default culture (which produced "¥1,000,000" instead of
    // "$1,000,000" the first time this ran, since decimal:C0 uses CultureInfo.Current).
    private static string FormatUsd(decimal amount) =>
        "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
}
