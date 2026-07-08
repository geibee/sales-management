module SalesManagement.Tests.Support.BatchFixture

open System
open Npgsql

/// Batch 系統合テストの共通 DB 接続文字列。
/// `TEST_DATABASE_URL` → `DATABASE_URL` → docker-compose の標準ローカル設定の順で解決する。
/// Batch ジョブは ApiFixture (Testcontainers) ではなく外部 Postgres プロセスへ直接接続するため
/// 専用の文字列を維持している。
/// `TEST_DATABASE_URL` が別にあるのは、`DATABASE_URL` はアプリ本体
/// (Program.resolveConnectionString) も最優先で読むため、プロセス全体に設定すると
/// ApiFixture (Testcontainers) 配下のアプリの接続先まで乗っ取ってしまうから。
/// 「テスト用外部 Postgres だけ」を別の場所へ逃がしたいときはこちらを使う。
let connectionString: string =
    [ "TEST_DATABASE_URL"; "DATABASE_URL" ]
    |> List.tryPick (fun name ->
        match Environment.GetEnvironmentVariable name with
        | null
        | "" -> None
        | url -> Some url)
    |> Option.defaultValue "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"

/// パラメータバインドつき非クエリ実行。
let execParam (sql: string) (parameters: (string * obj) list) =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    cmd.ExecuteNonQuery() |> ignore

/// 1 列 1 行のスカラ値を int64 として取り出す。null は 0L。
let queryScalarInt (sql: string) (parameters: (string * obj) list) : int64 =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    match cmd.ExecuteScalar() with
    | :? int64 as v -> v
    | :? int32 as v -> int64 v
    | null -> 0L
    | other -> Convert.ToInt64 other

/// 1 列 1 行のスカラ値を string Option として取り出す。
let queryScalarString (sql: string) (parameters: (string * obj) list) : string option =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    match cmd.ExecuteScalar() with
    | :? string as s -> Some s
    | _ -> None

/// 1 列複数行を int64 list として取り出す。
let queryIds (sql: string) (parameters: (string * obj) list) : int64 list =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    use rd = cmd.ExecuteReader()

    [ while rd.Read() do
          yield rd.GetInt64(0) ]

/// 指定 (year, location) の lot/lot_detail を全削除。
let cleanupLots (year: int) (location: string) =
    execParam
        "DELETE FROM lot_detail WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box year; "loc", box location ]

    execParam
        "DELETE FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box year; "loc", box location ]

/// batch_chunk_progress / batch_job_execution から (jobName, jobParams) の行を削除。
let cleanupJob (jobName: string) (jobParams: string) =
    execParam
        "DELETE FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
        [ "n", box jobName; "p", box jobParams ]

    execParam
        "DELETE FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
        [ "n", box jobName; "p", box jobParams ]

/// monthly-close ジョブ向けの cleanupJob ショートカット。
let cleanupMonthlyCloseJob (jobParams: string) = cleanupJob "monthly-close" jobParams

/// (jobName, jobParams) の batch_job_execution 行を RUNNING で投入する。
/// 既に存在する場合は ON CONFLICT DO NOTHING で無視する。
let ensureJobExecution (jobName: string) (jobParams: string) =
    execParam
        """
        INSERT INTO batch_job_execution (job_name, job_params, status)
        VALUES (@n, @p, 'RUNNING')
        ON CONFLICT (job_name, job_params) DO NOTHING
        """
        [ "n", box jobName; "p", box jobParams ]

/// 指定 (year, location) に manufactured 状態の lot を `count` 件 seed する。
/// `manufacturingCompletedDate` には 'YYYY-MM-DD' を渡す。投入された行の id を昇順で返す。
let seedManufacturedLotsReturningIds
    (year: int)
    (location: string)
    (count: int)
    (manufacturingCompletedDate: string)
    : int64 list =
    cleanupLots year location
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use tx = conn.BeginTransaction()

    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO lot (lot_number_year, lot_number_location, lot_number_seq,
                             division_code, department_code, section_code,
                             process_category, inspection_category, manufacturing_category,
                             status, manufacturing_completed_date)
            SELECT @y, @loc, seq,
                   1, 1, 1, 1, 1, 1,
                   'manufactured', @date::date
              FROM generate_series(1, @c) AS seq
            RETURNING id
            """,
            conn,
            tx
        )

    cmd.Parameters.AddWithValue("y", year) |> ignore
    cmd.Parameters.AddWithValue("loc", location) |> ignore
    cmd.Parameters.AddWithValue("c", count) |> ignore
    cmd.Parameters.AddWithValue("date", manufacturingCompletedDate) |> ignore
    use rd = cmd.ExecuteReader()

    let ids =
        [ while rd.Read() do
              yield rd.GetInt64(0) ]

    rd.Close()
    tx.Commit()
    ids

/// id の戻り値を捨てる seedManufacturedLots。
let seedManufacturedLots (year: int) (location: string) (count: int) (manufacturingCompletedDate: string) : unit =
    seedManufacturedLotsReturningIds year location count manufacturingCompletedDate
    |> ignore
