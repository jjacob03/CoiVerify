# CoiVerify — Certificate of Insurance (COI) parsing & validation

A .NET 10 modular monolith: one ASP.NET Core API host (`CoiVerify.Api`) over two
class libraries (`CoiVerify.Domain`, `CoiVerify.Infrastructure`). Not split into
real microservices on purpose — see the "Why one deployable, not microservices"
section below.

## Solution layout

```
CoiVerify.slnx
src/
  CoiVerify.Domain/          Schema (CertificateOfInsurance, CoverageLine),
                                 rules engine contracts (IRulesEvaluator), and the
                                 extraction contract (IDocumentExtractor). No
                                 dependencies on anything else in the solution.
  CoiVerify.Infrastructure/  DefaultRulesEvaluator (the generic validation
                                 engine) + two IDocumentExtractor implementations:
                                 StubDocumentExtractor (fake data, used today) and
                                 AzureDocumentExtractor (real pipeline, not wired up
                                 yet - see below).
  CoiVerify.Api/             Program.cs - minimal API exposing /health, /parse,
                                 /validate.
tests/
  CoiVerify.Tests/           Hand-rolled test runner (see "About the tests").
```

## Running it

```
cd src/CoiVerify.Api
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

`AzureDocumentExtractor` (in `CoiVerify.Infrastructure`) is the real
implementation: Azure AI Document Intelligence's `prebuilt-layout` model for OCR,
then an Azure OpenAI chat completion to map the OCR text into the
`CertificateOfInsurance` schema. `Program.cs` registers it automatically whenever
`DocIntel:Endpoint` is configured, and falls back to `StubDocumentExtractor`
otherwise - so the app still runs with zero setup, and picks up real extraction the
moment credentials exist. To turn it on:

1. Create an Azure AI Document Intelligence resource (the free F0 tier covers
   500 pages/month), get its endpoint + key.
2. Create an Azure OpenAI resource, deploy a chat-completion model to it (a small
   model is plenty for this), and note the resource endpoint, the deployment name,
   and a key.
3. Set the five config values via `dotnet user-secrets` (recommended for local dev
   - keeps keys out of source control) from `src/CoiVerify.Api`:
   ```
   dotnet user-secrets set "DocIntel:Endpoint" "https://<resource>.cognitiveservices.azure.com"
   dotnet user-secrets set "DocIntel:Key" "<doc-intelligence-key>"
   dotnet user-secrets set "Llm:Endpoint" "https://<resource>.openai.azure.com"
   dotnet user-secrets set "Llm:Key" "<azure-openai-key>"
   dotnet user-secrets set "Llm:DeploymentName" "<your-deployment-name>"
   ```
   In deployed environments, set the same keys as environment variables
   (`DocIntel__Endpoint`, etc.) instead. Don't commit real keys to
   `appsettings.json`.

To point this at a different LLM provider (Anthropic, OpenAI direct, etc.) instead
of Azure OpenAI, `MapWithLlmAsync` in `AzureDocumentExtractor.cs` is the one method
that needs to change - the URL, auth header, and request/response shape are all
provider-specific.

`AzureDocumentExtractor` is built on plain `HttpClient` + `System.Text.Json` against
the Document Intelligence and LLM REST APIs directly, not the `Azure.AI.*` NuGet
SDK packages. That was a constraint of the sandbox this was built in (see below),
but it's also a reasonable permanent choice: zero extra dependencies, and it stays
trivial to point at a different OCR or LLM vendor later - `MapWithLlmAsync` is the
only method that would need to change.

## About the tests

`tests/CoiVerify.Tests` is a plain console app with a small hand-rolled
assertion runner (`Run`/`Assert`), not xUnit or MSTest. That's not a style
preference - the sandbox this was built in blocks NuGet (`nuget.org` and GitHub's
NuGet proxy both return 403 from the egress policy), and every real .NET test
framework is NuGet-delivered. The whole solution was kept to zero external NuGet
packages for the same reason, so it builds and tests fully offline here.

On your own machine, with NuGet reachable, there's no reason to keep it this way -
swap `tests/CoiVerify.Tests` for a real xUnit project if you'd rather have
`[Fact]`/`[Theory]`, richer assertions, and IDE test-runner integration. The seven
cases in `Program.cs` translate directly to `[Fact]` methods; nothing about the
approach depends on the hand-rolled runner.

Run the tests:

```
dotnet run --project tests/CoiVerify.Tests
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
in `CoiVerify.Infrastructure` and `CoiVerify.Api`.

## Not yet built (next steps, in rough order)

- API key auth / RapidAPI proxy header verification on `/parse` and `/validate`.
- Stripe metered billing hookup for direct (non-RapidAPI) customers.
- Test `AzureDocumentExtractor` against actual ACORD 25 samples once you have real
  Azure credentials configured (blank templates are freely available - search
  "ACORD 25 blank form pdf").
- `GET /requirements/templates` - the prebuilt requirement-set library discussed
  as the real differentiator (general contractor subcontractor standard, property
  vendor standard, etc.).
- `POST /batch` for portfolio-level runs.
- Document retention policy: right now nothing persists the uploaded file or
  extracted content past the request - keep it that way (or add a short, explicit
  TTL) rather than accumulating other companies' data with no operational reason
  to keep it.
