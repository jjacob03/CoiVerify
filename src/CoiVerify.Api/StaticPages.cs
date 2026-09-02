namespace CoiVerify.Api;

/// <summary>
/// The static HTML pages served at "/" and "/terms". Kept as constants here (not
/// wwwroot files) so the whole app stays a single self-contained assembly with no
/// static-file middleware to configure - consistent with the zero-external-dependency
/// approach elsewhere in this project.
/// </summary>
public static class StaticPages
{
    private const string SharedStyles = """
        <style>
          :root { color-scheme: light dark; }
          body { font-family: -apple-system, Segoe UI, Roboto, sans-serif; max-width: 640px; margin: 64px auto; padding: 0 20px; line-height: 1.55; }
          h1 { margin-bottom: 4px; }
          h2 { font-size: 17px; margin-top: 36px; }
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
          .callout { background: rgba(212,167,44,0.15); border: 1px solid rgba(212,167,44,0.4); border-radius: 6px; padding: 12px 14px; font-size: 14px; }
        </style>
        """;

    public const string LandingPageHtml = $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>CoiVerify API</title>
        {{SharedStyles}}
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

          <footer>
            Source, full docs, and request format: <a href="https://github.com/jjacob03/CoiVerify">github.com/jjacob03/CoiVerify</a><br>
            <a href="/terms">Terms of Service</a>
          </footer>

          <script>
            fetch('/health').then(r => r.ok ? 'ok' : 'down').catch(() => 'down').then(state => {
              document.getElementById('dot').classList.add(state);
              document.getElementById('statusText').textContent = state === 'ok' ? 'Operational' : 'Unreachable';
            });
          </script>
        </body>
        </html>
        """;

    public const string TermsPageHtml = $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>CoiVerify - Terms of Service</title>
        {{SharedStyles}}
        </head>
        <body>
          <h1>Terms of Service</h1>
          <p class="tag">Last updated: September 2, 2026</p>

          <p>These Terms of Service ("Terms") govern access to and use of the CoiVerify
          API (the "Service"), provided by Justus Jacob, operating as CoiVerify ("we,"
          "us," "our"). By requesting an API key, or by sending any request to the
          Service, you ("you," "your," "Customer") agree to these Terms. If you do not
          agree, do not use the Service.</p>

          <h2>1. What the Service does</h2>
          <p>CoiVerify accepts an uploaded certificate-of-insurance document, uses
          automated optical character recognition ("OCR") and a large language model
          ("LLM") to extract structured data from it, and optionally evaluates that data
          against requirement rules you supply. The Service is a data-extraction and
          rules-evaluation tool. It does not provide insurance, legal, or compliance
          advice, and using it does not create any advisory, fiduciary, or professional
          relationship between you and us.</p>

          <div class="callout">
          <strong>2. No warranty on accuracy - read this part.</strong>
          <p style="margin-bottom:0">Extraction is performed by OCR and an LLM, both of
          which can misread, omit, or fabricate data, particularly on low-quality scans,
          non-standard forms, or unusual formatting. The Service is provided for
          informational purposes only. You are solely responsible for independently
          verifying any extracted data or compliance result before relying on it for any
          business, legal, financial, or insurance decision - including decisions about
          whether a vendor, contractor, or counterparty meets your insurance
          requirements. Do not use the Service as the sole basis for such a decision.</p>
          </div>

          <h2>3. Disclaimer of warranties</h2>
          <p>THE SERVICE IS PROVIDED "AS IS" AND "AS AVAILABLE," WITHOUT WARRANTY OF ANY
          KIND, EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY WARRANTY OF
          MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, ACCURACY, OR
          NON-INFRINGEMENT. WE DO NOT WARRANT THAT THE SERVICE WILL BE UNINTERRUPTED,
          ERROR-FREE, OR SECURE.</p>

          <h2>4. Limitation of liability</h2>
          <p>TO THE MAXIMUM EXTENT PERMITTED BY LAW, IN NO EVENT WILL WE BE LIABLE FOR
          ANY INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL, OR PUNITIVE DAMAGES, OR ANY
          LOSS OF PROFITS, REVENUE, DATA, OR BUSINESS OPPORTUNITY, ARISING OUT OF OR
          RELATED TO YOUR USE OF THE SERVICE, EVEN IF WE HAVE BEEN ADVISED OF THE
          POSSIBILITY OF SUCH DAMAGES. OUR TOTAL AGGREGATE LIABILITY FOR ANY CLAIM
          ARISING OUT OF OR RELATED TO THE SERVICE WILL NOT EXCEED THE GREATER OF (A)
          THE AMOUNT YOU PAID US FOR THE SERVICE IN THE THREE MONTHS PRECEDING THE
          CLAIM, OR (B) FIFTY U.S. DOLLARS ($50).</p>

          <h2>5. Data handling</h2>
          <p>As of this writing, the Service does not persist uploaded documents or
          extracted content beyond the lifetime of a single request - nothing is stored
          in a database or retained after a response is returned. This may change in the
          future (for example, to add usage logging or a batch-processing feature); if
          it does, this section will be updated first. Do not upload documents
          containing information beyond what's necessary to extract certificate-of-
          insurance data (for example, redact unrelated personal information where
          possible).</p>

          <h2>6. Acceptable use</h2>
          <p>You agree not to: share or publish your API key; attempt to circumvent rate
          limits or authentication; use the Service to build a competing certificate-
          extraction product using our outputs at scale without a separate agreement;
          or use the Service for any unlawful purpose.</p>

          <h2>7. Fees</h2>
          <p>Where the Service is offered under a paid plan, fees and billing terms will
          be presented separately (for example, at signup or in a pricing page) and are
          incorporated into these Terms by reference. Free or preview access may be
          limited, rate-limited, or discontinued at any time.</p>

          <h2>8. Termination</h2>
          <p>We may suspend or revoke API key access at any time, with or without
          notice, including for suspected abuse or non-payment. You may stop using the
          Service at any time.</p>

          <h2>9. Changes to these Terms</h2>
          <p>We may update these Terms from time to time; the "Last updated" date above
          will reflect the most recent change. Continued use of the Service after a
          change constitutes acceptance of the updated Terms.</p>

          <h2>10. Governing law</h2>
          <p>These Terms are governed by the laws of the State of New York, without
          regard to its conflict-of-laws principles, and any dispute arising from these
          Terms or the Service will be subject to the exclusive jurisdiction of the
          state and federal courts located in New York.</p>

          <h2>11. Contact</h2>
          <p>Questions about these Terms: <a href="mailto:justus.jacob@gmail.com">justus.jacob@gmail.com</a></p>

          <footer><a href="/">&larr; Back to CoiVerify</a></footer>
        </body>
        </html>
        """;
}
