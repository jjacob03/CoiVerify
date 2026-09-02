// Hand-rolled test runner, not xUnit/MSTest: this sandbox's egress policy blocks
// nuget.org, and xUnit/MSTest/NUnit are all NuGet-delivered test frameworks.
// Everything else in this solution (Domain/Infrastructure/Api) was deliberately kept
// to zero NuGet dependencies for the same reason, so the whole thing builds and tests
// fully offline here. On your own machine, with NuGet reachable, feel free to replace
// this file with a real xUnit/MSTest project instead - the assertions below translate
// directly to [Fact] methods.

using CoiVerify.Domain;
using CoiVerify.Infrastructure;

var failures = new List<string>();
var evaluator = new DefaultRulesEvaluator();
var today = new DateOnly(2026, 9, 2);

Run("All rules pass -> IsCompliant is true", () =>
{
    var cert = SampleCertificate();
    var request = new ValidationRequest
    {
        Rules =
        [
            new RequirementRule { Field = "GeneralLiability.EachOccurrence", Operator = ComparisonOperator.GreaterThanOrEqual, Value = "1000000" },
            new RequirementRule { Field = "AdditionalInsured", Operator = ComparisonOperator.Equal, Value = "true" },
            new RequirementRule { Field = "ExpirationDate", Operator = ComparisonOperator.OnOrAfter, Value = "today" }
        ]
    };

    var result = evaluator.Evaluate(cert, request, today);

    Assert(result.IsCompliant, "expected IsCompliant == true");
    Assert(result.RuleResults.All(r => r.Passed), "expected every rule to pass");
});

Run("Limit below requirement -> fails with actual/required in message", () =>
{
    var cert = SampleCertificate();
    var request = new ValidationRequest
    {
        Rules = [new RequirementRule { Field = "GeneralLiability.EachOccurrence", Operator = ComparisonOperator.GreaterThanOrEqual, Value = "5000000" }]
    };

    var result = evaluator.Evaluate(cert, request, today);

    Assert(!result.IsCompliant, "expected IsCompliant == false");
    var ruleResult = result.RuleResults.Single();
    Assert(!ruleResult.Passed, "expected the rule to fail");
    Assert(ruleResult.ActualValue == "1000000", $"expected actualValue '1000000', got '{ruleResult.ActualValue}'");
    Assert(ruleResult.Message?.Contains("$1,000,000") == true, $"expected USD-formatted message, got '{ruleResult.Message}'");
});

Run("Missing coverage line -> fails with a clear reason, not a crash", () =>
{
    var cert = SampleCertificate();
    var request = new ValidationRequest
    {
        Rules = [new RequirementRule { Field = "ProfessionalLiability.EachOccurrence", Operator = ComparisonOperator.GreaterThanOrEqual, Value = "1000000" }]
    };

    var result = evaluator.Evaluate(cert, request, today);

    Assert(!result.IsCompliant, "expected IsCompliant == false");
    Assert(result.RuleResults.Single().Message?.Contains("does not include") == true, "expected a 'does not include' message");
});

Run("Expired coverage -> ExpirationDate rule fails", () =>
{
    var cert = SampleCertificate() with
    {
        Coverages = SampleCertificate().Coverages
            .Select(c => c with { ExpirationDate = new DateOnly(2026, 1, 1) })
            .ToArray()
    };
    var request = new ValidationRequest
    {
        Rules = [new RequirementRule { Field = "ExpirationDate", Operator = ComparisonOperator.OnOrAfter, Value = "today" }]
    };

    var result = evaluator.Evaluate(cert, request, today);

    Assert(!result.IsCompliant, "expected an expired certificate to fail the ExpirationDate rule");
});

Run("Boolean field mismatch -> fails", () =>
{
    var cert = SampleCertificate() with { WaiverOfSubrogation = false };
    var request = new ValidationRequest
    {
        Rules = [new RequirementRule { Field = "WaiverOfSubrogation", Operator = ComparisonOperator.Equal, Value = "true" }]
    };

    var result = evaluator.Evaluate(cert, request, today);

    Assert(!result.IsCompliant, "expected WaiverOfSubrogation mismatch to fail");
});

RunAsync("StubDocumentExtractor round-trips a non-empty upload", async () =>
{
    var extractor = new StubDocumentExtractor();
    await using var stream = new MemoryStream("fake pdf bytes"u8.ToArray());

    var result = await extractor.ExtractAsync(stream, "test.pdf", "application/pdf");

    Assert(result.Success, "expected extraction to succeed for a non-empty file");
    Assert(result.Document is not null, "expected a document to be returned");
    Assert(result.Document!.Coverages.Count == 3, $"expected 3 coverage lines, got {result.Document.Coverages.Count}");
});

RunAsync("StubDocumentExtractor rejects an empty upload", async () =>
{
    var extractor = new StubDocumentExtractor();
    await using var stream = new MemoryStream();

    var result = await extractor.ExtractAsync(stream, "empty.pdf", "application/pdf");

    Assert(!result.Success, "expected extraction to fail for an empty file");
});

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine($"FAILED: {failures.Count} test(s) failed.");
    foreach (var f in failures) Console.WriteLine($"  - {f}");
    return 1;
}

Console.WriteLine("All tests passed.");
return 0;

// --- helpers -------------------------------------------------------------------

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {ex.Message}");
        failures.Add(name);
    }
}

void RunAsync(string name, Func<Task> test) => Run(name, () => test().GetAwaiter().GetResult());

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

CertificateOfInsurance SampleCertificate() => new()
{
    InsuredName = "Acme Contracting Inc",
    AdditionalInsured = true,
    WaiverOfSubrogation = true,
    Coverages =
    [
        new CoverageLine
        {
            Type = CoverageType.GeneralLiability,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Limits = new Dictionary<string, decimal> { ["EachOccurrence"] = 1_000_000m }
        },
        new CoverageLine
        {
            Type = CoverageType.AutomobileLiability,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Limits = new Dictionary<string, decimal> { ["CombinedSingleLimit"] = 1_000_000m }
        }
    ]
};
