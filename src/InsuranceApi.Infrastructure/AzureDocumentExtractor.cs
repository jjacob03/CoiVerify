using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InsuranceApi.Domain;

namespace InsuranceApi.Infrastructure;

/// <summary>
/// Real extraction pipeline: Azure AI Document Intelligence "prebuilt-layout" for OCR
/// (raw text + tables + key/value pairs), then an LLM call to map that raw output into
/// the CertificateOfInsurance schema. Deliberately built on plain HttpClient +
/// System.Text.Json against the REST APIs directly, rather than the Azure.AI.*
/// NuGet SDK packages - keeps this project buildable with zero NuGet restore, and
/// makes it trivial to point at a different OCR or LLM provider later (Claude,
/// OpenAI direct, etc.) by changing this one class.
///
/// NOT wired into DI yet - Program.cs still registers StubDocumentExtractor. Swap it
/// in once you have:
///   1. An Azure AI Document Intelligence resource (endpoint + key), OR any OCR
///      service that returns text/layout for a document.
///   2. An LLM endpoint (Azure OpenAI, OpenAI, Anthropic, etc.) capable of structured
///      JSON output, and a prompt that maps OCR text -> the CertificateOfInsurance
///      shape below.
/// See README.md "Wiring up real extraction" for the exact config keys expected here.
/// </summary>
public sealed class AzureDocumentExtractor(HttpClient httpClient, AzureDocumentExtractorOptions options)
    : IDocumentExtractor
{
    public async Task<ExtractionResult> ExtractAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var layoutText = await RunDocumentIntelligenceAsync(documentStream, contentType, cancellationToken);
            var document = await MapWithLlmAsync(layoutText, cancellationToken);

            return new ExtractionResult { Success = true, Document = document };
        }
        catch (Exception ex)
        {
            // Real implementation should distinguish "OCR found nothing" (bad
            // upload) from transient failures (retry) from auth failures (fail
            // loud) - collapsed here for brevity.
            return new ExtractionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// POSTs to Document Intelligence's "prebuilt-layout" analyze endpoint and polls
    /// the operation until done, per the documented async analyze pattern. Returns the
    /// raw extracted text/tables as a single string ready to hand to the LLM step.
    /// https://learn.microsoft.com/azure/ai-services/document-intelligence/
    /// </summary>
    private async Task<string> RunDocumentIntelligenceAsync(
        Stream documentStream, string contentType, CancellationToken cancellationToken)
    {
        var analyzeUrl =
            $"{options.DocumentIntelligenceEndpoint.TrimEnd('/')}" +
            "/documentintelligence/documentModels/prebuilt-layout:analyze" +
            "?api-version=2024-11-30";

        using var content = new StreamContent(documentStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl) { Content = content };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.DocumentIntelligenceKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Document Intelligence returns 202 + an Operation-Location header to poll.
        var operationLocation = response.Headers.GetValues("Operation-Location").First();

        while (true)
        {
            await Task.Delay(500, cancellationToken);

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
            pollRequest.Headers.Add("Ocp-Apim-Subscription-Key", options.DocumentIntelligenceKey);
            using var pollResponse = await httpClient.SendAsync(pollRequest, cancellationToken);
            pollResponse.EnsureSuccessStatusCode();

            var payload = await pollResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var status = payload.GetProperty("status").GetString();

            if (status == "succeeded")
            {
                // analyzeResult.content is the concatenated plain-text layout - good
                // enough as LLM input; walk analyzeResult.tables for structured
                // limit/date grids if you want higher accuracy than plain text gives.
                return payload.GetProperty("analyzeResult").GetProperty("content").GetString() ?? "";
            }

            if (status == "failed")
            {
                throw new InvalidOperationException("Document Intelligence analysis failed.");
            }
            // status == "running" / "notStarted" -> keep polling.
        }
    }

    /// <summary>
    /// Sends the OCR text to an LLM with instructions to return the
    /// CertificateOfInsurance shape as JSON, and deserializes the result.
    /// The exact request shape depends on which LLM endpoint you point this at
    /// (Azure OpenAI chat completions, Anthropic messages API, etc.) - this method is
    /// the one place that needs to change to switch providers.
    /// </summary>
    private async Task<CertificateOfInsurance> MapWithLlmAsync(string layoutText, CancellationToken cancellationToken)
    {
        var prompt = BuildExtractionPrompt(layoutText);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.LlmEndpoint);
        request.Headers.Add("Authorization", $"Bearer {options.LlmApiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { prompt, response_format = "json" }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // TODO: replace with real deserialization once the LLM endpoint/shape is
        // chosen - the model should be prompted to emit exactly this schema.
        return JsonSerializer.Deserialize<CertificateOfInsurance>(json)
            ?? throw new InvalidOperationException("LLM did not return a parseable certificate.");
    }

    private static string BuildExtractionPrompt(string layoutText) => $"""
        Extract the following fields from this certificate of insurance (ACORD 25)
        OCR text and return them as JSON matching the CertificateOfInsurance schema:
        producer, insured, certificate holder, additional-insured and
        waiver-of-subrogation checkboxes, and every coverage line present (General
        Liability, Auto Liability, Umbrella, Workers Comp, Professional) with policy
        number, carrier, effective/expiration dates, and every dollar limit shown.
        If a field is not present on the certificate, omit it rather than guessing.

        OCR TEXT:
        {layoutText}
        """;
}

public sealed class AzureDocumentExtractorOptions
{
    public required string DocumentIntelligenceEndpoint { get; init; }
    public required string DocumentIntelligenceKey { get; init; }
    public required string LlmEndpoint { get; init; }
    public required string LlmApiKey { get; init; }
}
