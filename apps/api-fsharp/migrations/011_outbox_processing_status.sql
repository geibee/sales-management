-- Outbox claim pattern: allow 'processing' as an interim status so that
-- multiple workers can claim disjoint rows via SELECT ... FOR UPDATE SKIP LOCKED
-- without double-publishing the same event.
--
-- The status column is TEXT with no CHECK constraint, so 'processing' is
-- already permitted; we only add an index that helps reaper / observability
-- queries that look at currently-claimed rows.
CREATE INDEX IF NOT EXISTS idx_outbox_processing
    ON outbox_events (status, processed_at)
    WHERE status = 'processing';
