using InsuranceApi.Domain;

namespace InsuranceApi.Infrastructure;

/// <summary>
/// Fake extractor so the API is runnable end-to-end with no external dependencies or
/// credentials. Returns a fixed, realistic ACORD 25 payload regardless of what's
/// uploaded (it still reads the stream, so upload plumbing is genuinely exercised).
/// Swap this for <see cref="AzureDocumentExtractor"/> (or your own implementation)
/// once real Document Intelligence + LLM credentials are available - nothing else in
/// the Api or Domain layers needs to change, since both implement IDocumentExtractor.
/// </summary>
public sealed class StubDocumentExtractor : IDocumentExtractor
{
    public async Task<ExtractionResult> ExtractAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // Actually consume the stream so callers exercise real upload handling,
        // even though we discard the bytes in this stub.
        using var ms = new MemoryStream();
        await documentStream.CopyToAsync(ms, cancellationToken);

        if (ms.Length == 0)
        {
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = "Uploaded file was empty."
            };
        }

        var document = new CertificateOfInsurance
        {
            ProducerName = "Sample Insurance Agency LLC",
            ProducerAddress = "123 Main St, Springfield, IL 62701",
            InsuredName = "Acme Contracting Inc",
            InsuredAddress = "456 Industrial Pkwy, Springfield, IL 62704",
            CertificateHolderName = "Example Property Management Co",
            CertificateHolderAddress = "789 Commerce Dr, Springfield, IL 62702",
            AdditionalInsured = true,
            WaiverOfSubrogation = true,
            DescriptionOfOperations = "General contracting services per master service agreement.",
            Coverages =
            [
                new CoverageLine
                {
                    Type = CoverageType.GeneralLiability,
                    InsurerName = "Sample Mutual Insurance Co",
                    InsurerLetter = "A",
                    PolicyNumber = "GL-2026-001234",
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    ExpirationDate = new DateOnly(2027, 1, 1),
                    Limits = new Dictionary<string, decimal>
                    {
                        ["EachOccurrence"] = 1_000_000m,
                        ["GeneralAggregate"] = 2_000_000m,
                        ["ProductsCompletedOpsAggregate"] = 2_000_000m,
                        ["PersonalAndAdvertisingInjury"] = 1_000_000m
                    }
                },
                new CoverageLine
                {
                    Type = CoverageType.AutomobileLiability,
                    InsurerName = "Sample Mutual Insurance Co",
                    InsurerLetter = "A",
                    PolicyNumber = "AUTO-2026-005678",
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    ExpirationDate = new DateOnly(2027, 1, 1),
                    Limits = new Dictionary<string, decimal>
                    {
                        ["CombinedSingleLimit"] = 1_000_000m
                    }
                },
                new CoverageLine
                {
                    Type = CoverageType.WorkersCompEmployersLiability,
                    InsurerName = "Sample Casualty Co",
                    InsurerLetter = "B",
                    PolicyNumber = "WC-2026-009999",
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    ExpirationDate = new DateOnly(2027, 1, 1),
                    Limits = new Dictionary<string, decimal>
                    {
                        ["EachAccident"] = 1_000_000m,
                        ["DiseasePolicyLimit"] = 1_000_000m,
                        ["DiseaseEachEmployee"] = 1_000_000m
                    }
                }
            ]
        };

        return new ExtractionResult
        {
            Success = true,
            Document = document,
            Warnings = ["This is stub data from StubDocumentExtractor - not a real extraction."]
        };
    }
}
