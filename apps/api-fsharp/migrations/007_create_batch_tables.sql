-- バッチジョブ管理テーブル
-- Spring Batch のメタデータ5テーブル (JOB_INSTANCE, JOB_EXECUTION, STEP_EXECUTION, STEP_EXECUTION_CONTEXT, JOB_EXECUTION_PARAMS) を
-- 自前管理の2テーブルに簡素化する。

CREATE TABLE batch_job_execution (
    job_name        TEXT NOT NULL,
    job_params      TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'RUNNING',
    started_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at    TIMESTAMPTZ,
    read_count      INT NOT NULL DEFAULT 0,
    write_count     INT NOT NULL DEFAULT 0,
    skip_count      INT NOT NULL DEFAULT 0,
    error_message   TEXT,
    PRIMARY KEY (job_name, job_params)
);

CREATE TABLE batch_chunk_progress (
    job_name          TEXT NOT NULL,
    job_params        TEXT NOT NULL,
    partition_id      INT NOT NULL DEFAULT 0,
    last_processed_id BIGINT NOT NULL,
    processed_count   INT NOT NULL DEFAULT 0,
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (job_name, job_params, partition_id),
    FOREIGN KEY (job_name, job_params) REFERENCES batch_job_execution(job_name, job_params)
);
