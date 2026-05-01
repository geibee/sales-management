-- パーティション並列実行で「完了済みパーティション」と「未着手パーティション」を区別するためのステータス。
-- 既存の単一スレッド経路は status='RUNNING' のみを使い、完了時に行ごと delete する。
-- パーティション経路は完了時に status='COMPLETED' を立て、全パーティション完了後に一括 delete する。
ALTER TABLE batch_chunk_progress
    ADD COLUMN status TEXT NOT NULL DEFAULT 'RUNNING';
