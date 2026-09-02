using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CoiVerify.Api;
using CoiVerify.Domain;
using CoiVerify.Infrastructure;

const string LandingPageHtml = """
    <!doctype html>
    <html lang="en">
    <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>CoiVerify API</title>
    <style>
      :root { color-scheme: light dark; }
      body { font-family: -apple-system, Segoe UI, Roboto, sans-serif; max-width: 640px; margin: 64px auto; padding: 0 20px; line-height: 1.55; }
      h1 { margin-bottom: 4px; }
      .tag { color: #888; font-size: 14px; margin-top: 0; }
      code, pre { background: rgba(127,127,127,0.15); border-radius: 4px; }
      code { padding: 2px 5px; }
      pre { padding: 12px; overflow-x: auto; }
      table { border-collapse: collapse; width: 100%; margin: 16px 0; }
      th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid rgba(127,127,127,0.25); vertical-align: top; font-size: 14px; }
      th { font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; color: #888; }
      .status { display: inline-flex; align-items: center; gap: 6px; font-size: 14px; }
      .dot { width: 8px; height: 8px; border-radius: 50%; background: #888; display: inline-block; }
      .dot.ok { background: #2ea043; }
      .dot.down { background: #cf222e; }
      a { color: inherit; }
      footer { margin-top: 40px; font-size: 13px; color: #888; }
    </style>
    </head>
    <body>
      <h1>CoiVerify</h1>
      <p class="tag">Certificate-of-insurance (ACORD 25) parsing &amp; compliance-validation API</p>
      <p class="status"><span class="dot" id="dot"></span><span id="statusText">Checking status&hellip;</span></p>

      <p>Private preview &mdash; every request to <code>/parse</code> and <code>/validate</code>
      requires an API key. Ask the project owner for one.</p>

      <table>
        <tr><th>Route</th><th>What it does</th></tr>
        <tr><td><code>GET /health</code></td><td>Liveness check, no key required.</td></tr>
        <tr><td><code>POST /parse</code></td><td>Upload a COI PDF, get back structured extraction. No compliance check.</td></tr>
        <tr><td><code>POST /validate</code></td><td>Upload a COI PDF plus a set of requirement rules, get back extraction + pass/fail per rule.</td></tr>
      </table>

      <pre><code>curl -X POST https://coiverify-api.azurewebsites.net/parse \
      -H "X-Api-Key: &lt;your key&gt;" \
      -F "file=@sample.pdf;type=application/pdf"</code></pre>

      <footer>Source, full docs, and request format: <a href="https://github.com/jjacob03/CoiVerify">github.com/jjacob03/CoiVerify</a></footer>

      <script>
        fetch('/health').then(r => r.ok ? 'ok' : 'down').catch(() => 'down').then(state => {
          document.getElementById('dot').classList.add(state);
          document.getElementById('statusText').textContent = state === 'ok' ? 'Operational' : 'Unreachable';
        });
      </script>
    </body>
    </html>
    """;

var builder = WebApplication.CreateBuilder(args);

// Enums as strings ("GeneralLiability", not 0) - much friendlier for API consumers,
// and matches the field-path strings the rules engine already uses.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.WriteIndented = false;
});

// Real extraction pipeline: Azure AI Document Intelligence for OCR, then an Azure
// OpenAI chat completion to map that text into the CertificateOfInsurance schema.
// Config comes from user-secrets locally (dotnet user-secrets set "DocIntel:Endpoint" ...)
// or environment variables in deployed environments - see README.md "Wiring up real
// extraction". Falls back to StubDocumentExtractor if DocIntel:Endpoint isn't configured,
// so the app still runs out of the box without credentials.
if (!string.IsNullOrWhiteSpace(builder.Configuration["DocIntel:Endpoint"]))
{
    builder.Services.AddHttpClient<AzureDocumentExtractor>();
    builder.Services.AddSingleton(new AzureDocumentExtractorOptions
    {
        DocumentIntelligenceEndpoint = builder.Configuration["DocIntel:Endpoint"]!,
        DocumentIntelligenceKey = builder.Configuration["DocIntel:Key"]!,
        LlmEndpoint = builder.Configuration["Llm:Endpoint"]!,
        LlmApiKey = builder.Configuration["Llm:Key"]!,
        LlmDeploymentName = builder.Configuration["Llm:DeploymentName"]!
    });
    builder.Services.AddSingleton<IDocumentExtractor, AzureDocumentExtractor>();
}
else
{
    builder.Services.AddSingleton<IDocumentExtractor, StubDocumentExtractor>();
}
builder.Services.AddSingleton<IRulesEvaluator, DefaultRulesEvaluator>();
builder.Services.AddSingleton(TimeProvider.System);

// Each /parse and /validate call costs real money (Document Intelligence + LLM), so
// each API key gets its own 20-requests-per-minute budget - stops a stray script or a
// leaked key from running up an unbounded bill. Partitioned by the raw header value,
// not the validated key, so it runs ahead of ApiKeyAuthFilter and a flood of invalid
// keys can't dodge the limiter either.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PerApiKey", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Request.Headers["X-Api-Key"].ToString(),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/", () => Results.Content(LandingPageHtml, "text/html"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// --- POST /parse -------------------------------------------------------------------
// multipart/form-data with a "file" field. Returns the raw extraction, no validation.
app.MapPost("/parse", async (IFormFile? file, IDocumentExtractor extractor, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Upload a file using the 'file' form field." });
    }

    await using var stream = file.OpenReadStream();
    var result = await extractor.ExtractAsync(stream, file.FileName, file.ContentType, ct);

    return result.Success
        ? Results.Ok(result)
        : Results.UnprocessableEntity(result);
})
.DisableAntiforgery()
.AddEndpointFilter<ApiKeyAuthFilter>()
.RequireRateLimiting("PerApiKey");

// --- POST /validate ------------------------------------------------------------------
// multipart/form-data with a "file" field plus a "rules" field containing a
// ValidationRequest as JSON, e.g.:
//   {"rules":[
//     {"field":"GeneralLiability.EachOccurrence","operator":"GreaterThanOrEqual","value":"1000000"},
//     {"field":"AdditionalInsured","operator":"Equal","value":"true"},
//     {"field":"ExpirationDate","operator":"OnOrAfter","value":"today"}
//   ]}
app.MapPost("/validate", async (
    IFormFile? file,
    HttpRequest request,
    IDocumentExtractor extractor,
    IRulesEvaluator evaluator,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Upload a file using the 'file' form field." });
    }

    var rulesJson = request.Form["rules"].ToString();
    if (string.IsNullOrWhiteSpace(rulesJson))
    {
        return Results.BadRequest(new { error = "Include a 'rules' form field containing a ValidationRequest JSON object." });
    }

    ValidationRequest validationRequest;
    try
    {
        validationRequest = JsonSerializer.Deserialize<ValidationRequest>(rulesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        }) ?? throw new JsonException("Empty rules payload.");
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Could not parse 'rules' as JSON: {ex.Message}" });
    }

    await using var stream = file.OpenReadStream();
    var extraction = await extractor.ExtractAsync(stream, file.FileName, file.ContentType, ct);

    if (!extraction.Success || extraction.Document is null)
    {
        return Results.UnprocessableEntity(new { extraction });
    }

    var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    var validation = evaluator.Evaluate(extraction.Document, validationRequest, asOf);

    return Results.Ok(new { extraction, validation });
})
.DisableAntiforgery()
.AddEndpointFilter<ApiKeyAuthFilter>()
.RequireRateLimiting("PerApiKey");

app.Run();
