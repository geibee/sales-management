-- Outbox pattern: domain events persisted in the same transaction as business data.
CREATE TABLE outbox_events (
    id           BIGSERIAL PRIMARY KEY,
    event_type   TEXT NOT NULL,
    payload      JSONB NOT NULL,
    status       TEXT NOT NULL DEFAULT 'pending',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ,
    error_detail TEXT
);

CREATE INDEX idx_outbox_pending ON outbox_events (status) WHERE status = 'pending';
