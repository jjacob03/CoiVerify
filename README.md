# Insurance API — Certificate of Insurance (COI) parsing & validation

A .NET 10 modular monolith: one ASP.NET Core API host (`InsuranceApi.Api`) over two
class libraries (`InsuranceApi.Domain`, `InsuranceApi.Infrastructure`). Not split into
real microservices on purpose — see the "Why one deployable, not microservices"
section below.

## Solution layout

```
InsuranceApi.slnx
src/
  InsuranceApi.Domain/          Schema (CertificateOfInsurance, CoverageLine),
                                 rules engine contracts (IRulesEvaluator), and the
                                 extraction contract (IDocumentExtractor). No
                                 dependencies on anything else in the solution.
  InsuranceApi.Infrastructure/  DefaultRulesEvaluator (the generic validation
                                 engine) + two IDocumentExtractor implementations:
                                 StubDocumentExtractor (fake data, used today) and
                                 AzureDocumentExtractor (real pipeline, not wired up
                                 yet - see below).
  InsuranceApi.Api/             Program.cs - minimal API exposing /health, /parse,
                                 /validate.
tests/
  InsuranceApi.Tests/           Hand-rolled test runner (see "About the tests").
```

## Running it

```
cd src/InsuranceApi.Api
dotnet run
```

Then, from another terminal:

```
curl http://localhost:5264/health

curl -X POST http://localhost:5264/parse \
  -F "file=@sample.pdf;type=application/pdf"

curl -X POST http://localhost:5264/validate \
  -F "file=@sample.pdf;type=application/pdf" \
  -F 'rules={"rules":[
        {"field":"GeneralLiability.EachOccurrence","operator":"GreaterThanOrEqual","value":"1000000"},
        {"field":"AdditionalInsured","operator":"Equal","value":"true"},
        {"field":"ExpirationDate","operator":"OnOrAfter","value":"today"}
      ]}'
```

Right now every `/parse` and `/validate` call returns the same canned certificate
data regardless of what you upload (`StubDocumentExtractor`) - the upload plumbing,
schema, and rules engine are all real and tested; only the OCR/LLM step is faked.
That's deliberate: it lets the whole pipeline, and everything you build against it,
be exercised end-to-end before you've spent a dollar on Document Intelligence or an
LLM.

## Wiring up real extraction

`AzureDocumentExtractor` (in `InsuranceApi.Infrastructure`) is the real
implementation: Azure AI Document Intelligence's `prebuilt-layout` model for OCR,
then an LLM call to map the OCR text into the `CertificateOfInsurance` schema. It's
written but not registered in `Program.cs` yet. To turn it on:

1. Create an Azure AI Document Intelligence resource, get its endpoint + key.
2. Pick an LLM endpoint (Azure OpenAI, OpenAI, Anthropic, etc.) and get its
   endpoint + key. `MapWithLlmAsync` in `AzureDocumentExtractor.cs` has a `// TODO`
   at the exact spot to adjust the request/response shape for whichever provider
   you choose.
3. In `Program.cs`, replace:
   ```csharp
   builder.Services.AddSingleton<IDocumentExtractor, StubDocumentExtractor>();
   ```
   with:
   ```csharp
   builder.Services.AddHttpClient<AzureDocumentExtractor>();
   builder.Services.AddSingleton(new AzureDocumentExtractorOptions
   {
       DocumentIntelligenceEndpoint = builder.Configuration["DocIntel:Endpoint"]!,
       DocumentIntelligenceKey = builder.Configuration["DocIntel:Key"]!,
       LlmEndpoint = builder.Configuration["Llm:Endpoint"]!,
       LlmApiKey = builder.Configuration["Llm:Key"]!
   });
   builder.Services.AddSingleton<IDocumentExtractor, AzureDocumentExtractor>();
   ```
   and add the four config values via `dotnet user-secrets` (recommended for local
   dev - keeps keys out of source control) or environment variables. Don't commit
   real keys to `appsettings.json`.

`AzureDocumentExtractor` is built on plain `HttpClient` + `System.Text.Json` against
the Document Intelligence and LLM REST APIs directly, not the `Azure.AI.*` NuGet
SDK packages. That was a constraint of the sandbox this was built in (see below),
but it's also a reasonable permanent choice: zero extra dependencies, and it stays
trivial to point at a different OCR or LLM vendor later - `MapWithLlmAsync` is the
only method that would need to change.

## About the tests

`tests/InsuranceApi.Tests` is a plain console app with a small hand-rolled
assertion runner (`Run`/`Assert`), not xUnit or MSTest. That's not a style
preference - the sandbox this was built in blocks NuGet (`nuget.org` and GitHub's
NuGet proxy both return 403 from the egress policy), and every real .NET test
framework is NuGet-delivered. The whole solution was kept to zero external NuGet
packages for the same reason, so it builds and tests fully offline here.

On your own machine, with NuGet reachable, there's no reason to keep it this way -
swap `tests/InsuranceApi.Tests` for a real xUnit project if you'd rather have
`[Fact]`/`[Theory]`, richer assertions, and IDE test-runner integration. The seven
cases in `Program.cs` translate directly to `[Fact]` methods; nothing about the
approach depends on the hand-rolled runner.

Run the tests:

```
dotnet run --project tests/InsuranceApi.Tests
```

## Why one deployable, not microservices

Domain/Infrastructure/Api are separate *projects* (clean boundaries, independently
testable) but one *deployable* - a modular monolith, not microservices. Splitting
into independently-deployed services buys you independent scaling and independent
ownership, at the cost of network calls, service discovery, and distributed
tracing to manage. Neither benefit applies yet at MVP/solo-founder stage, so the
cost isn't worth paying. The natural first candidate to actually split out later is
the extraction step (`IDocumentExtractor`) behind a queue, once batch volume
justifies scaling it independently from the request/response API - the interface
boundary is already there for exactly that.

## The generalization story (loss runs, applications, etc.)

The parts of this solution that are COI-specific are narrow on purpose:
`CertificateOfInsurance`/`CoverageLine` in Domain, and the field-path strings the
rules engine understands (`"GeneralLiability.EachOccurrence"`, etc.). Everything
else - the API host, the upload handling, `DefaultRulesEvaluator`'s comparison
logic, the extraction pipeline shape (OCR call -> LLM mapping call) - is generic.
A second product (loss run parsing, ACORD 125/130 applications, etc.) means a new
schema type, a new extraction prompt, and new API routes, reusing everything else
in `InsuranceApi.Infrastructure` and `InsuranceApi.Api`.

## Not yet built (next steps, in rough order)

- API key auth / RapidAPI proxy header verification on `/parse` and `/validate`.
- Stripe metered billing hookup for direct (non-RapidAPI) customers.
- Swap `StubDocumentExtractor` for `AzureDocumentExtractor` once you have real
  credentials, and test against actual ACORD 25 samples (blank templates are
  freely available - search "ACORD 25 blank form pdf").
- `GET /requirements/templates` - the prebuilt requirement-set library discussed
  as the real differentiator (general contractor subcontractor standard, property
  vendor standard, etc.).
- `POST /batch` for portfolio-level runs.
- Document retention policy: right now nothing persists the uploaded file or
  extracted content past the request - keep it that way (or add a short, explicit
  TTL) rather than accumulating other companies' data with no operational reason
  to keep it.
