module SalesManagement.Tests.IntegrationTests.BatchJobLifecycleTests

open System
open System.Threading
open System.Threading.Tasks
open Npgsql
open Xunit
open SalesManagement.Infrastructure

let private connectionString =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

let private execParam (sql: string) (parameters: (string * obj) list) =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    cmd.ExecuteNonQuery() |> ignore

let private queryScalarInt (sql: string) (parameters: (string * obj) list) : int64 =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    let result = cmd.ExecuteScalar()

    match result with
    | :? int64 as v -> v
    | :? int32 as v -> int64 v
    | null -> 0L
    | other -> Convert.ToInt64 other

let private queryScalarString (sql: string) (parameters: (string * obj) list) : string option =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    match cmd.ExecuteScalar() with
    | :? string as s -> Some s
    | _ -> None

let private testYear = 2098
let private testLocation = "T3"

let private cleanupLots () =
    execParam
        "DELETE FROM lot_detail WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

    execParam
        "DELETE FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

let private cleanupJobExecution (jobParams: string) =
    execParam
        "DELETE FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
        [ "n", box "monthly-close"; "p", box jobParams ]

    execParam
        "DELETE FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
        [ "n", box "monthly-close"; "p", box jobParams ]

let private seedManufacturedLots (count: int) =
    cleanupLots ()
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
                   'manufactured', '2098-04-01'
              FROM generate_series(1, @c) AS seq
            """,
            conn,
            tx
        )

    cmd.Parameters.AddWithValue("y", testYear) |> ignore
    cmd.Parameters.AddWithValue("loc", testLocation) |> ignore
    cmd.Parameters.AddWithValue("c", count) |> ignore
    cmd.ExecuteNonQuery() |> ignore
    tx.Commit()

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``runMonthlyCloseManaged records COMPLETED with read/write counts on success`` () =
    let jobParams = sprintf "%d-04" testYear

    try
        seedManufacturedLots 30

        let outcome = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 jobParams

        match outcome with
        | MonthlyCloseBatch.Completed o ->
            Assert.True(o.TotalRead >= 30)
            Assert.True(o.TotalProcessed >= 30)
        | other -> Assert.Fail(sprintf "expected Completed, got %A" other)

        let status =
            queryScalarString
                "SELECT status FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(Some "COMPLETED", status)

        let readCount =
            queryScalarInt
                "SELECT read_count FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.True(readCount >= 30L, sprintf "expected read_count ≥ 30, got %d" readCount)

        let writeCount =
            queryScalarInt
                "SELECT write_count FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.True(writeCount >= 30L, sprintf "expected write_count ≥ 30, got %d" writeCount)
    finally
        cleanupJobExecution jobParams
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``re-execution after COMPLETED is rejected with AlreadyCompleted`` () =
    let jobParams = sprintf "%d-05" testYear

    try
        seedManufacturedLots 10

        let first = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 jobParams

        match first with
        | MonthlyCloseBatch.Completed _ -> ()
        | other -> Assert.Fail(sprintf "expected first run Completed, got %A" other)

        let second = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 jobParams

        Assert.Equal(MonthlyCloseBatch.AlreadyCompleted, second)
    finally
        cleanupJobExecution jobParams
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``different job_params can run independently`` () =
    let p1 = sprintf "%d-06" testYear
    let p2 = sprintf "%d-07" testYear

    try
        seedManufacturedLots 5

        let first = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 p1

        match first with
        | MonthlyCloseBatch.Completed _ -> ()
        | other -> Assert.Fail(sprintf "expected Completed for %s, got %A" p1 other)

        // 別パラメータは別ジョブインスタンスとして受理される (lot は既に出荷指示済なので件数 0 でも COMPLETED)
        let second = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 p2

        match second with
        | MonthlyCloseBatch.Completed _ -> ()
        | other -> Assert.Fail(sprintf "expected Completed for %s, got %A" p2 other)
    finally
        cleanupJobExecution p1
        cleanupJobExecution p2
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``concurrent execution is rejected by tryStart`` () =
    let jobParams = sprintf "%d-08" testYear

    try
        // RUNNING 行を直接 INSERT して "実行中" の状態を再現する
        execParam
            """
            INSERT INTO batch_job_execution (job_name, job_params, status)
            VALUES (@n, @p, 'RUNNING')
            """
            [ "n", box "monthly-close"; "p", box jobParams ]

        let outcome =
            JobExecutionRepository.tryStart connectionString "monthly-close" jobParams

        Assert.Equal(JobExecutionRepository.AlreadyRunning, outcome)
    finally
        cleanupJobExecution jobParams

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``failure during batch records FAILED with error_message`` () =
    let jobParams = sprintf "%d-09" testYear

    try
        // 不正な targetMonth は invalidArg → 例外で failJob 経由で FAILED になることを確認
        // ここでは tryStart→fail フローを直接呼ぶ
        let started =
            JobExecutionRepository.tryStart connectionString "monthly-close" jobParams

        Assert.Equal(JobExecutionRepository.Started, started)

        JobExecutionRepository.fail connectionString "monthly-close" jobParams "intentional failure"

        let status =
            queryScalarString
                "SELECT status FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(Some "FAILED", status)

        let errorMessage =
            queryScalarString
                "SELECT error_message FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(Some "intentional failure", errorMessage)
    finally
        cleanupJobExecution jobParams

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``FAILED job can be restarted via tryStart`` () =
    let jobParams = sprintf "%d-10" testYear

    try
        execParam
            """
            INSERT INTO batch_job_execution (job_name, job_params, status, error_message)
            VALUES (@n, @p, 'FAILED', 'previous failure')
            """
            [ "n", box "monthly-close"; "p", box jobParams ]

        let outcome =
            JobExecutionRepository.tryStart connectionString "monthly-close" jobParams

        Assert.Equal(JobExecutionRepository.Started, outcome)

        let status =
            queryScalarString
                "SELECT status FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(Some "RUNNING", status)
    finally
        cleanupJobExecution jobParams

[<Fact>]
[<Trait("Category", "BatchJobLifecycle")>]
let ``parallel tryStart for same FAILED row results in exactly one Started`` () =
    let jobParams = sprintf "%d-11" testYear

    try
        execParam
            """
            INSERT INTO batch_job_execution (job_name, job_params, status, error_message)
            VALUES (@n, @p, 'FAILED', 'previous failure')
            """
            [ "n", box "monthly-close"; "p", box jobParams ]

        let task1 =
            Task.Run(fun () -> JobExecutionRepository.tryStart connectionString "monthly-close" jobParams)

        let task2 =
            Task.Run(fun () -> JobExecutionRepository.tryStart connectionString "monthly-close" jobParams)

        Task.WaitAll([| task1 :> Task; task2 :> Task |])

        let outcomes = [ task1.Result; task2.Result ]

        let started =
            outcomes
            |> List.filter (fun o -> o = JobExecutionRepository.Started)
            |> List.length

        Assert.Equal(1, started)
    finally
        cleanupJobExecution jobParams
