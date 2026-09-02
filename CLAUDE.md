# CoiVerify

Certificate-of-insurance (ACORD 25) parsing and compliance-validation API. Built by
Justus, an insurance-industry software engineer, as the first product in what's meant
to become a small platform of insurance-document APIs (loss runs, ACORD applications,
etc. would follow the same pattern later).

Read README.md first for architecture, how to run it, and what's built vs. stubbed -
this file is the business/strategy context that isn't in there.

## Why this product, specifically

Researched RapidAPI's "insurance" category (109 results): roughly 40% turned out to be
SEO spam or non-functional listings, not real APIs. Real coverage clusters around
vehicle/VIN data and property/flood risk - the latter is now genuinely saturated
(5-6 competitors repackaging free FEMA data at $1.99/mo, launched within months of
each other - avoid that space). Zero results for certificate-of-insurance parsing,
loss run parsing, or producer license verification. Cross-checked against the real
competitor landscape (NetVendor, myCOI, Certificial, SmartCompliance, TrackMyVendor,
getjones) - all heavy enterprise SaaS platforms, none offer a lightweight self-serve
API a smaller developer could embed. That gap is the actual opportunity.

Two adjacent ideas came up and were deliberately set aside for now:
- Workers' comp classification / experience-mod lookup and producer license
  verification are real needs too, but the underlying data (NCCI, state rating
  bureaus, NIPR) is proprietary and licensed. Do not build against it without
  confirming redistribution rights first - that's a legal question, not a technical
  one, and it's the kind of thing that kills a product after launch, not before.

## Distribution strategy already decided

- Multi-home, not exclusive to one channel: list on RapidAPI (free to list, but they
  take a flat 25% cut) while treating a direct channel (own site + Stripe metered
  billing, ~3% all-in cost) as the real primary channel - the fee gap matters most on
  larger direct B2B contracts, not marketplace self-serve traffic.
- AWS Marketplace (3% public listings, down to 1.5% on renewals/larger private
  offers) is worth adding once there's revenue proof - a lot of the actual buyers
  (proptech vendor-compliance tools, GC compliance teams) already have AWS
  procurement relationships.
- Revenue is realistically a slow ramp - comparable RapidAPI providers report 6-12
  months to real traction, and the swing factor is landing 1-2 direct contracts, not
  marketplace volume. Treat that as the honest planning assumption, not a pessimistic
  one.

## Business setup timing

No LLC or insurance needed to prototype or onboard early low-stakes users. The
trigger to get both in place is "about to sign a real commercial contract where
dollars ride on the output being correct" - not a revenue threshold. A liability
disclaimer in the API's terms of service, though, is free and should exist before
the first paying customer, not after.

## Naming

Landed on "CoiVerify" over a few alternatives - notably passed on "Binder" despite
liking the wordplay, because "binder" already means something different and specific
in P&C (temporary evidence of coverage, or MGA binding authority), which would
confuse the exact insurance-professional audience this is selling to.

## Current state

Scaffolded and tested (7/7 passing) in a cloud sandbox that blocked NuGet entirely -
that's why the test project is a hand-rolled runner instead of xUnit, and worth
swapping to real xUnit here now that NuGet works normally. The Azure Document
Intelligence + LLM extraction client uses plain HttpClient instead of the Azure SDK
NuGet packages - that one's a deliberate choice independent of the sandbox
restriction (fewer dependencies, trivial to swap OCR/LLM vendors later), not just a
workaround, so no strong need to change it. See README.md "Not yet built" for the
prioritized next-steps list.
