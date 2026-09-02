namespace CoiVerify.Domain;

/// <summary>
/// Turns an uploaded document (image or PDF bytes) into a structured
/// <see cref="CertificateOfInsurance"/>. Swappable: today this is backed by a stub
/// implementation returning canned data; the real implementation calls Azure AI
/// Document Intelligence for OCR/layout and an LLM to map the result into this
/// schema (see CoiVerify.Infrastructure).
/// </summary>
public interface IDocumentExtractor
{
    Task<ExtractionResult> ExtractAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
