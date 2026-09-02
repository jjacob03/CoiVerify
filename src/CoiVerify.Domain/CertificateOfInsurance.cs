namespace CoiVerify.Domain;

/// <summary>
/// The structured, extracted contents of a certificate of insurance (ACORD 25 shape).
/// This is the schema every extraction implementation (stub, Azure Document
/// Intelligence + LLM, or a future alternative) must map into, and the schema the
/// rules engine evaluates requirements against.
/// </summary>
public sealed record CertificateOfInsurance
{
    public string? ProducerName { get; init; }
    public string? ProducerAddress { get; init; }
    public string? InsuredName { get; init; }
    public string? InsuredAddress { get; init; }

    public IReadOnlyList<CoverageLine> Coverages { get; init; } = Array.Empty<CoverageLine>();

    public string? CertificateHolderName { get; init; }
    public string? CertificateHolderAddress { get; init; }

    public bool AdditionalInsured { get; init; }
    public bool WaiverOfSubrogation { get; init; }
    public string? DescriptionOfOperations { get; init; }

    public CoverageLine? GetCoverage(CoverageType type) =>
        Coverages.FirstOrDefault(c => c.Type == type);
}

/// <summary>
/// Wraps a <see cref="CertificateOfInsurance"/> with extraction-quality metadata, since
/// OCR/LLM extraction is never 100% certain and callers deserve to know that.
/// </summary>
public sealed record ExtractionResult
{
    public required bool Success { get; init; }
    public CertificateOfInsurance? Document { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }
}
