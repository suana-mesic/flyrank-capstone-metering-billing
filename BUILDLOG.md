# BUILDLOG — AI-usage log

Honesty is graded, not perfection. This log records where AI helped, where it was
wrong, and what I changed.

---

## Where AI helped

- Scaffolding the idempotent metering path: `UNIQUE(tenant_id, idempotency_key)` plus
  `INSERT ... ON CONFLICT DO NOTHING`, so a retried request records exactly one usage event.
- The quota boundary logic returning honest status codes — `402 Payment Required` at the API-call
  limit, `429 Too Many Requests` over the token limit, each with an explanatory message.
- The pricing model as a pinned config record (per-call, per-input, per-cached-input, per-output)
  with cached input at half the input rate and reasoning tokens folded into output.
- The Stripe webhook handler: signature verification with `EventUtility.ConstructEvent` and
  deduplication by `stripe_event_id` in `processed_webhook_events`.

## Where AI was wrong / what I changed

- **`/usage` priced every token the same at first.** The AI's initial rollup summed all tokens into
  one bucket, so cached-input and reasoning tokens were mispriced on real usage (only the pinned unit
  test was correct). I split the stored usage into input / cached-input / output columns and had
  `/usage` price each category through `PricingService`, so the live rollup matches the pricing rules.
- **Locale decimal bug.** `decimal.TryParse` under a Bosnian (comma-decimal) locale misread the
  pinned rates. I moved all decimal parsing/formatting to `NumberStyles.Float` +
  `CultureInfo.InvariantCulture`.
- **Webhook API-version mismatch.** `ConstructEvent` threw on a test-mode API version mismatch until
  I passed `throwOnApiVersionMismatch: false`, keeping signature verification strict while tolerating
  the CLI's version.

## What I verified myself

- `dotnet test` green — 10 tests: pricing (pinned + cached + reasoning), quota (under / at / over),
  idempotent usage, forged-webhook rejection, duplicate-webhook dedup.
- Ran the Stripe test-mode Checkout with `stripe listen` / `stripe trigger`: the webhook flipped a
  tenant Free → Pro, and replaying the same event processed it only once.
- Sent a forged webhook (bad signature) → `400`, nothing changed.

## Cost & grounding

- No AI model is called in this capstone — the "AI tokens" are metering numbers, not real model
  usage, so there is no API key and no per-call spend. Pricing is pure arithmetic over pinned rates.
