module SalesManagement.Infrastructure.ChunkProgressRepository

open Npgsql

/// 前回の中断位置を返す。レコードが無ければ 0L。
let getLastProcessedId (connectionString: string) (jobName: string) (jobParams: string) : int64 =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            """
            SELECT last_processed_id
              FROM batch_chunk_progress
             WHERE job_name = @n
               AND job_params = @p
               AND partition_id = 0
            """,
            conn
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore

    match cmd.ExecuteScalar() with
    | :? int64 as v -> v
    | :? int32 as v -> int64 v
    | null -> 0L
    | _ -> 0L

/// 業務データ更新と同一トランザクション内で進捗を UPSERT する (partition_id = 0)。
let upsertProgress
    (tx: NpgsqlTransaction)
    (jobName: string)
    (jobParams: string)
    (lastProcessedId: int64)
    (processedCount: int)
    : unit =
    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO batch_chunk_progress
                (job_name, job_params, partition_id, last_processed_id, processed_count, status, updated_at)
            VALUES (@n, @p, 0, @lid, @pc, 'RUNNING', NOW())
            ON CONFLICT (job_name, job_params, partition_id)
            DO UPDATE SET last_processed_id = EXCLUDED.last_processed_id,
                          processed_count   = EXCLUDED.processed_count,
                          status            = 'RUNNING',
                          updated_at        = NOW()
            """,
            tx.Connection,
            tx
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("lid", lastProcessedId) |> ignore
    cmd.Parameters.AddWithValue("pc", processedCount) |> ignore
    cmd.ExecuteNonQuery() |> ignore

/// 完了したジョブの進捗を削除する (全パーティションを含む)。
let deleteProgress (connectionString: string) (jobName: string) (jobParams: string) : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand("DELETE FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p", conn)

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.ExecuteNonQuery() |> ignore

type PartitionProgress =
    { LastProcessedId: int64
      ProcessedCount: int
      Status: string }

/// 指定パーティションの進捗を取得する。レコードが無ければ None。
let tryGetPartitionProgress
    (connectionString: string)
    (jobName: string)
    (jobParams: string)
    (partitionId: int)
    : PartitionProgress option =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            """
            SELECT last_processed_id, processed_count, status
              FROM batch_chunk_progress
             WHERE job_name = @n
               AND job_params = @p
               AND partition_id = @pid
            """,
            conn
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("pid", partitionId) |> ignore

    use rd = cmd.ExecuteReader()

    if rd.Read() then
        Some
            { LastProcessedId = rd.GetInt64(0)
              ProcessedCount = rd.GetInt32(1)
              Status = rd.GetString(2) }
    else
        None

/// パーティション単位の進捗を業務データ更新と同一トランザクションで UPSERT する。
let upsertPartitionProgress
    (tx: NpgsqlTransaction)
    (jobName: string)
    (jobParams: string)
    (partitionId: int)
    (lastProcessedId: int64)
    (processedCount: int)
    : unit =
    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO batch_chunk_progress
                (job_name, job_params, partition_id, last_processed_id, processed_count, status, updated_at)
            VALUES (@n, @p, @pid, @lid, @pc, 'RUNNING', NOW())
            ON CONFLICT (job_name, job_params, partition_id)
            DO UPDATE SET last_processed_id = EXCLUDED.last_processed_id,
                          processed_count   = EXCLUDED.processed_count,
                          status            = 'RUNNING',
                          updated_at        = NOW()
            """,
            tx.Connection,
            tx
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("pid", partitionId) |> ignore
    cmd.Parameters.AddWithValue("lid", lastProcessedId) |> ignore
    cmd.Parameters.AddWithValue("pc", processedCount) |> ignore
    cmd.ExecuteNonQuery() |> ignore

/// パーティション完了マーク。並列実行中の他パーティションが残っているうちは行を保持し、
/// 全パーティション完了後に呼び出し元 (PartitionedBatch) が deleteProgress でまとめて削除する。
let markPartitionCompleted
    (connectionString: string)
    (jobName: string)
    (jobParams: string)
    (partitionId: int)
    (lastProcessedId: int64)
    (processedCount: int)
    : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO batch_chunk_progress
                (job_name, job_params, partition_id, last_processed_id, processed_count, status, updated_at)
            VALUES (@n, @p, @pid, @lid, @pc, 'COMPLETED', NOW())
            ON CONFLICT (job_name, job_params, partition_id)
            DO UPDATE SET last_processed_id = EXCLUDED.last_processed_id,
                          processed_count   = EXCLUDED.processed_count,
                          status            = 'COMPLETED',
                          updated_at        = NOW()
            """,
            conn
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("pid", partitionId) |> ignore
    cmd.Parameters.AddWithValue("lid", lastProcessedId) |> ignore
    cmd.Parameters.AddWithValue("pc", processedCount) |> ignore
    cmd.ExecuteNonQuery() |> ignore
