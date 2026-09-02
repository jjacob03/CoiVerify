using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoiVerify.Domain;

namespace CoiVerify.Infrastructure;

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
    /// Sends the OCR text to an Azure OpenAI chat completions deployment with
    /// instructions to return the CertificateOfInsurance shape as JSON, and
    /// deserializes the result. Azure OpenAI is the one provider assumed here -
    /// swap this method (URL, auth header, request/response shape) to point at a
    /// different provider (Anthropic messages API, OpenAI direct, etc.) instead.
    /// </summary>
    private async Task<CertificateOfInsurance> MapWithLlmAsync(string layoutText, CancellationToken cancellationToken)
    {
        var analyzeUrl =
            $"{options.LlmEndpoint.TrimEnd('/')}" +
            $"/openai/deployments/{Uri.EscapeDataString(options.LlmDeploymentName)}/chat/completions" +
            "?api-version=2024-10-21";

        using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl);
        request.Headers.Add("api-key", options.LlmApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                messages = new[]
                {
                    new { role = "system", content = ExtractionSystemPrompt },
                    new { role = "user", content = BuildExtractionPrompt(layoutText) }
                },
                response_format = new { type = "json_object" },
                temperature = 0
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var content = payload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("LLM response had no message content.");

        return JsonSerializer.Deserialize<CertificateOfInsurance>(content, LlmResponseJsonOptions)
            ?? throw new InvalidOperationException("LLM did not return a parseable certificate.");
    }

    private static readonly JsonSerializerOptions LlmResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string ExtractionSystemPrompt = """
        You extract structured data from certificate-of-insurance (ACORD 25) OCR text.
        Respond with a single JSON object and nothing else, matching this shape:
        {
          "ProducerName": string|null, "ProducerAddress": string|null,
          "InsuredName": string|null, "InsuredAddress": string|null,
          "CertificateHolderName": string|null, "CertificateHolderAddress": string|null,
          "AdditionalInsured": bool, "WaiverOfSubrogation": bool,
          "DescriptionOfOperations": string|null,
          "Coverages": [
            {
              "Type": "GeneralLiability"|"AutomobileLiability"|"UmbrellaExcessLiability"
                      |"WorkersCompEmployersLiability"|"ProfessionalLiability"|"Other",
              "InsurerName": string|null, "InsurerLetter": string|null,
              "PolicyNumber": string|null,
              "EffectiveDate": "yyyy-MM-dd"|null, "ExpirationDate": "yyyy-MM-dd"|null,
              "Limits": { "<LimitName>": number, ... }
            }
          ]
        }
        For each coverage line's "Limits", use exactly these key names for that coverage
        type - never the certificate's own printed label text (e.g. write "EachAccident",
        not "E.L. Each Accident"). Only include a limit key if the certificate actually
        shows a dollar amount for it.
          GeneralLiability: EachOccurrence, DamageToRentedPremises, MedExp,
            PersonalAndAdvInjury, GeneralAggregate, ProductsCompOpAgg
          AutomobileLiability: CombinedSingleLimit, BodilyInjuryPerPerson,
            BodilyInjuryPerAccident, PropertyDamage
          UmbrellaExcessLiability: EachOccurrence, Aggregate
          WorkersCompEmployersLiability: EachAccident, DiseaseEachEmployee,
            DiseasePolicyLimit
          ProfessionalLiability: EachClaim, Aggregate
          Other: use your best judgment - there's no fixed set for this one.
        Only include a coverage line if the certificate actually shows it. Omit fields
        you can't find rather than guessing at a value.
        """;

    private static string BuildExtractionPrompt(string layoutText) => $"""
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
    public required string LlmDeploymentName { get; init; }
}
