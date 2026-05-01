-- Audit columns: who created / last updated each row, and when.
-- created_by / updated_by are populated from the JWT 'sub' claim;
-- when auth is disabled (e.g. local dev) the application falls back to 'system'.
ALTER TABLE lot ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE lot ADD COLUMN created_by TEXT NOT NULL DEFAULT 'system';
ALTER TABLE lot ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE lot ADD COLUMN updated_by TEXT NOT NULL DEFAULT 'system';
