CREATE TABLE IF NOT EXISTS plans (
    id              SERIAL PRIMARY KEY,
    name            TEXT NOT NULL UNIQUE,
    api_call_limit  INT NOT NULL,
    token_limit     INT NOT NULL
);

CREATE TABLE IF NOT EXISTS tenants (
    id              SERIAL PRIMARY KEY,
    email           TEXT NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    plan_id         INT NOT NULL DEFAULT 1 REFERENCES plans(id),
    stripe_customer_id TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS subscription_status TEXT DEFAULT 'none';

CREATE TABLE IF NOT EXISTS usage_events (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT NOT NULL REFERENCES tenants(id),
    usage_type      TEXT NOT NULL,
    quantity        INT NOT NULL,
    idempotency_key TEXT NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(tenant_id, idempotency_key)
);

-- Token cost breakdown (added after the initial design): lets /usage price
-- input / cached-input / output separately, so the cached-input and reasoning
-- rules apply to real monthly usage, not only in the pinned unit test.
ALTER TABLE usage_events ADD COLUMN IF NOT EXISTS input_tokens        INT NOT NULL DEFAULT 0;
ALTER TABLE usage_events ADD COLUMN IF NOT EXISTS cached_input_tokens INT NOT NULL DEFAULT 0;
ALTER TABLE usage_events ADD COLUMN IF NOT EXISTS output_tokens       INT NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS subscriptions (
    id                      SERIAL PRIMARY KEY,
    tenant_id               INT NOT NULL REFERENCES tenants(id),
    stripe_subscription_id  TEXT NOT NULL UNIQUE,
    stripe_event_id         TEXT,
    status                  TEXT NOT NULL DEFAULT 'active',
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS processed_webhook_events (
    stripe_event_id TEXT PRIMARY KEY,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO plans (name, api_call_limit, token_limit)
VALUES ('Free', 1000, 100000), ('Pro', 10000, 1000000)
ON CONFLICT (name) DO NOTHING;