# EVIDENCE

One proof per Definition-of-Done checkbox (§6 of the brief). A claim without evidence scores as not done.

Run tests with `dotnet test`. Run the system with `docker compose up -d` +
`dotnet run --project BillingApi` (base URL `http://localhost:5095`). Most boxes are proven by the
10-test suite; the runtime boxes give the exact curl to run against a registered tenant.

---

## Metering

- [x] **A billable action creates exactly one usage event, even under retries — deduplicated by idempotency key.**
  Proof: the `usage_events` table has `UNIQUE(tenant_id, idempotency_key)` and the repository inserts
  with `ON CONFLICT DO NOTHING`, returning the original result on a duplicate. Test
  `TryRecord_WithSameIdempotencyKey_RecordsUsageOnlyOnce`.
  ```
  Test summary: total: 10; failed: 0; succeeded: 10; skipped: 0    (UsageRepositoryTests included)
  ```
  Live reproduction — same idempotency key twice → one event:
  ```
  POST /meter  {"usageType":"api_call","quantity":1,"idempotencyKey":"k-123"}
  → {"recorded":true,"message":"Usage recorded","used":1,"limit":1000}

  POST /meter  {"usageType":"api_call","quantity":1,"idempotencyKey":"k-123"}   (same key)
  → {"recorded":false,"message":"Duplicate — already recorded","used":1,"limit":1000}
  ```
  The second call is refused as a duplicate — usage stays at 1, not 2.

- [x] **A test proves double-counting cannot happen.**
  Proof: `TryRecord_WithSameIdempotencyKey_RecordsUsageOnlyOnce` records twice with the same key and
  asserts the usage count increments once. Green in the suite above.

## Quotas

- [x] **Usage is checked against the tenant's plan; requests over the limit are rejected.**
  Proof: `QuotaService` compares current + requested usage against the plan limits before allowing a
  billable action. Tests `Check_JustUnderApiCallLimit_IsAllowed`, `Check_BothUnderLimits_IsAllowed`
  (allowed) and the two refusal tests below.

- [x] **Responses carry the correct status codes (429 / 402) and a message explaining why.**
  Proof: over the API-call quota → `429 Too Many Requests` (quota exceeded); over the token quota →
  `402 Payment Required` (upgrade needed), each with an explanatory JSON message (`used`, `limit`,
  `requested`). Tests `Check_AtApiCallLimit_IsRefusedWith429Semantics` and
  `Check_OverTokenLimit_IsRefusedWith402Semantics`.
  ```
  # Free plan = 1000 api_call / 100000 token limit. One over-limit call proves the boundary.
  POST /meter  {"usageType":"api_call","quantity":1001,...}
  → HTTP/1.1 429 Too Many Requests
    {"error":"Quota exceeded for api_call","used":1,"limit":1000,"requested":1001}

  POST /meter  {"usageType":"token","quantity":100001,...}
  → HTTP/1.1 402 Payment Required
    {"error":"Quota exceeded for token","used":0,"limit":100000,"requested":100001}
  ```

## Cost calculation

- [x] **Monthly usage rolls up into a cost figure per tenant.**
  Proof: `GET /usage` aggregates the tenant's `usage_events` for the month and returns the plan,
  period, per-category usage vs limit, and the estimated cost from `PricingService`:
  ```
  GET /usage
  → {"plan":"Free","period":"2026-08",
     "usage":{"api_calls":{"used":1,"limit":1000},"tokens":{"used":0,"limit":100000}},
     "estimatedCost":0.00100000}
  ```
  One api_call at `0.001`/call = `0.001` — the rollup priced it exactly.

- [x] **AI token pricing handles cached input tokens, reasoning tokens, and output pricing correctly.**
  Proof: cached input tokens are priced at half of normal input (`0.00000075` vs `0.0000015`), and
  reasoning tokens are billed as output, not as a separate category. Tests
  `CachedInputTokens_AreCheaperThanNormalInputTokens` and
  `ReasoningTokens_AreBilledAsOutput_NotAsSeparateCategory`.
  ```
  Test summary: total: 10; failed: 0; succeeded: 10; skipped: 0    (PricingServiceTests included)
  ```

- [x] **Pricing constants are pinned and covered by tests.**
  Proof: constants pinned in `PricingService.Config` (`PricePerApiCall: 0.001`,
  `PricePerInputToken: 0.0000015`, `PricePerCachedInputToken: 0.00000075`,
  `PricePerOutputToken: 0.000006`), asserted by `CalculateCost_WithPinnedInputs_ReturnsExactExpectedCost`.

## Stripe integration

- [ ] **Subscription checkout works end-to-end in Stripe test mode.**
  Proof: `POST /billing/checkout` creates a Checkout Session for the Pro price; completing it in test
  mode fires `checkout.session.completed` → the webhook flips the tenant Free → Pro. Reproduce with
  the Stripe CLI:
  ```
  [RUN & PASTE]
  stripe listen --forward-to localhost:5095/webhooks/stripe
  # complete the returned Checkout URL in test mode, then:
  curl -s http://localhost:5095/usage -H "Authorization: Bearer TOKEN"   # plan now Pro, new limits
  ```

- [x] **Webhooks verify signatures, ignore duplicate events, and update tenant plan/status.**
  Proof: the handler verifies the Stripe signature (a forged one is rejected `400`) and deduplicates
  by `stripe_event_id` (PRIMARY KEY in `processed_webhook_events`, replay processed once). Tests
  `ForgedWebhook_WithInvalidSignature_IsRejected` and `TryMarkProcessed_WithSameEventId_IsProcessedOnlyOnce`.
  ```
  Test summary: total: 10; failed: 0; succeeded: 10; skipped: 0    (WebhookSignatureTests + WebhookRepositoryTests)
  ```

## Data model, tests & documentation

- [x] **Database includes tenants, plans, subscriptions, and usage events; customer data isolated per tenant.**
  Proof: `Database/init.sql` — `plans`, `tenants` (`plan_id` FK), `usage_events`
  (`tenant_id` FK, `UNIQUE(tenant_id, idempotency_key)`), `subscriptions`
  (`stripe_subscription_id` UNIQUE), `processed_webhook_events` (`stripe_event_id` PRIMARY KEY).
  Every query is scoped by `tenant_id`, so one tenant never sees another's usage.

- [x] **Tests cover: duplicate usage prevention, quota boundary cases (at / just under / over), cost calculations, invalid-webhook rejection, duplicate-webhook handling.**
  Proof: `dotnet test` — 10 tests across `PricingServiceTests`, `QuotaServiceTests`,
  `UsageRepositoryTests`, `WebhookSignatureTests`, `WebhookRepositoryTests`.
  ```
  Test summary: total: 10; failed: 0; succeeded: 10; skipped: 0
  ```

- [x] **README + architecture diagram + setup instructions; submission-pack files present.**
  Proof: `README.md` + `architecture-diagram.png`, plus `capstone.yaml`, `EVIDENCE.md`,
  `BUILDLOG.md`, `.env.example`, and `LICENSE`.
