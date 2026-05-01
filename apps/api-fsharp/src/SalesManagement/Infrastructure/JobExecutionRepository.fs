module SalesManagement.Infrastructure.JobExecutionRepository

open Npgsql

type StartOutcome =
    | Started
    | AlreadyRunning
    | AlreadyCompleted

let private restartFailed (conn: NpgsqlConnection) (jobName: string) (jobParams: string) : int =
    use cmd =
        new NpgsqlCommand(
            """
            UPDATE batch_job_execution
               SET status = 'RUNNING',
                   started_at = NOW(),
                   completed_at = NULL,
                   error_message = NULL,
                   read_count = 0,
                   write_count = 0,
                   skip_count = 0
             WHERE job_name = @name
               AND job_params = @params
               AND status = 'FAILED'
            """,
            conn
        )

    cmd.Parameters.AddWithValue("name", jobName) |> ignore
    cmd.Parameters.AddWithValue("params", jobParams) |> ignore
    cmd.ExecuteNonQuery()

let private insertNew (conn: NpgsqlConnection) (jobName: string) (jobParams: string) : int =
    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO batch_job_execution (job_name, job_params, status)
            VALUES (@name, @params, 'RUNNING')
            ON CONFLICT (job_name, job_params) DO NOTHING
            """,
            conn
        )

    cmd.Parameters.AddWithValue("name", jobName) |> ignore
    cmd.Parameters.AddWithValue("params", jobParams) |> ignore
    cmd.ExecuteNonQuery()

let private currentStatus (conn: NpgsqlConnection) (jobName: string) (jobParams: string) : string option =
    use cmd =
        new NpgsqlCommand("SELECT status FROM batch_job_execution WHERE job_name = @n AND job_params = @p", conn)

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore

    match cmd.ExecuteScalar() with
    | :? string as s -> Some s
    | _ -> None

/// 起動時の判定:
/// 1. FAILED であれば 1 文の UPDATE で RUNNING に戻し、リスタート扱いにする
/// 2. レコードがなければ INSERT (ON CONFLICT DO NOTHING) で行を確保する
/// 3. それ以外は現在のステータスを見て AlreadyRunning / AlreadyCompleted を返す
let tryStart (connectionString: string) (jobName: string) (jobParams: string) : StartOutcome =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    if restartFailed conn jobName jobParams > 0 then
        Started
    elif insertNew conn jobName jobParams > 0 then
        Started
    else
        match currentStatus conn jobName jobParams with
        | Some "RUNNING" -> AlreadyRunning
        | Some "COMPLETED" -> AlreadyCompleted
        | Some "FAILED" -> AlreadyRunning // 別プロセスが先に FAILED→RUNNING へ昇格させた直後
        | _ -> AlreadyRunning

let complete
    (connectionString: string)
    (jobName: string)
    (jobParams: string)
    (readCount: int)
    (writeCount: int)
    (skipCount: int)
    : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            """
            UPDATE batch_job_execution
               SET status = 'COMPLETED',
                   completed_at = NOW(),
                   read_count = @r,
                   write_count = @w,
                   skip_count = @s,
                   error_message = NULL
             WHERE job_name = @n AND job_params = @p
            """,
            conn
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("r", readCount) |> ignore
    cmd.Parameters.AddWithValue("w", writeCount) |> ignore
    cmd.Parameters.AddWithValue("s", skipCount) |> ignore
    cmd.ExecuteNonQuery() |> ignore

let fail (connectionString: string) (jobName: string) (jobParams: string) (errorMessage: string) : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            """
            UPDATE batch_job_execution
               SET status = 'FAILED',
                   completed_at = NOW(),
                   error_message = @msg
             WHERE job_name = @n AND job_params = @p
            """,
            conn
        )

    cmd.Parameters.AddWithValue("n", jobName) |> ignore
    cmd.Parameters.AddWithValue("p", jobParams) |> ignore
    cmd.Parameters.AddWithValue("msg", errorMessage) |> ignore
    cmd.ExecuteNonQuery() |> ignore
