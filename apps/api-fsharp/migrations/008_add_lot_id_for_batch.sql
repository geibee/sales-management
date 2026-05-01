-- バッチ処理でチャンク化したページネーション (WHERE id > lastId ORDER BY id LIMIT N) を行うための
-- 連番カラムを追加する。既存行には PostgreSQL が自動採番する。
ALTER TABLE lot ADD COLUMN id BIGSERIAL UNIQUE NOT NULL;
CREATE INDEX IF NOT EXISTS idx_lot_status_id ON lot (status, id);
