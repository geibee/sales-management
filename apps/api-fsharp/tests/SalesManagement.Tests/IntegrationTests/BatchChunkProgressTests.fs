module SalesManagement.Tests.IntegrationTests.BatchChunkProgressTests

open System
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

    match cmd.ExecuteScalar() with
    | :? int64 as v -> v
    | :? int32 as v -> int64 v
    | null -> 0L
    | other -> Convert.ToInt64 other

let private queryIds (sql: string) (parameters: (string * obj) list) : int64 list =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    use rd = cmd.ExecuteReader()

    [ while rd.Read() do
          yield rd.GetInt64(0) ]

let private testYear = 2097
let private testLocation = "T4"

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
                   'manufactured', '2097-04-01'
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
[<Trait("Category", "BatchChunkProgress")>]
let ``batch_chunk_progress is deleted after successful run`` () =
    let jobParams = sprintf "%d-04" testYear

    try
        seedManufacturedLots 25

        let outcome = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 jobParams

        match outcome with
        | MonthlyCloseBatch.Completed _ -> ()
        | other -> Assert.Fail(sprintf "expected Completed, got %A" other)

        let progressRows =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(0L, progressRows)
    finally
        cleanupJobExecution jobParams
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``restart from FAILED with chunk_progress skips already-processed lots`` () =
    let jobParams = sprintf "%d-05" testYear

    try
        seedManufacturedLots 50

        let ids =
            queryIds
                "SELECT id FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc ORDER BY id"
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(50, List.length ids)
        let midId = ids.[24]

        execParam
            """
            INSERT INTO batch_job_execution (job_name, job_params, status, error_message)
            VALUES (@n, @p, 'FAILED', 'simulated previous failure')
            """
            [ "n", box "monthly-close"; "p", box jobParams ]

        execParam
            """
            INSERT INTO batch_chunk_progress
                (job_name, job_params, partition_id, last_processed_id, processed_count)
            VALUES (@n, @p, 0, @lid, 25)
            """
            [ "n", box "monthly-close"; "p", box jobParams; "lid", box midId ]

        let outcome = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 10 jobParams

        match outcome with
        | MonthlyCloseBatch.Completed o ->
            Assert.True(o.TotalRead >= 25, sprintf "expected TotalRead ≥ 25, got %d" o.TotalRead)

            Assert.True(o.TotalProcessed >= 25, sprintf "expected TotalProcessed ≥ 25, got %d" o.TotalProcessed)
        | other -> Assert.Fail(sprintf "expected Completed, got %A" other)

        let stillManufactured =
            queryScalarInt
                """
                SELECT count(*) FROM lot
                 WHERE lot_number_year = @y AND lot_number_location = @loc
                   AND status = 'manufactured' AND id <= @mid
                """
                [ "y", box testYear; "loc", box testLocation; "mid", box midId ]

        Assert.Equal(25L, stillManufactured)

        let shippingInstructed =
            queryScalarInt
                """
                SELECT count(*) FROM lot
                 WHERE lot_number_year = @y AND lot_number_location = @loc
                   AND status = 'shipping_instructed' AND id > @mid
                """
                [ "y", box testYear; "loc", box testLocation; "mid", box midId ]

        Assert.Equal(25L, shippingInstructed)

        let progressRows =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(0L, progressRows)
    finally
        cleanupJobExecution jobParams
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``upsertProgress within rolled-back transaction does not persist`` () =
    let jobParams = sprintf "%d-06" testYear

    try
        execParam
            """
            INSERT INTO batch_job_execution (job_name, job_params, status)
            VALUES (@n, @p, 'RUNNING')
            """
            [ "n", box "monthly-close"; "p", box jobParams ]

        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        use tx = conn.BeginTransaction()
        ChunkProgressRepository.upsertProgress tx "monthly-close" jobParams 999L 999
        tx.Rollback()

        let progressRows =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(0L, progressRows)
    finally
        cleanupJobExecution jobParams

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``getLastProcessedId returns 0 when no progress row exists`` () =
    let jobParams = sprintf "%d-07" testYear

    try
        let lastId =
            ChunkProgressRepository.getLastProcessedId connectionString "monthly-close" jobParams

        Assert.Equal(0L, lastId)
    finally
        cleanupJobExecution jobParams

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``progress is upserted (not duplicated) across multiple chunks`` () =
    let jobParams = sprintf "%d-08" testYear

    try
        seedManufacturedLots 30

        let outcome = MonthlyCloseBatch.runMonthlyCloseManaged connectionString 5 jobParams

        match outcome with
        | MonthlyCloseBatch.Completed o -> Assert.True(o.ChunkCount >= 6)
        | other -> Assert.Fail(sprintf "expected Completed, got %A" other)

        // 完了後は deleteProgress が走るので 0 件のはず。中間で重複行が残らないこと
        let progressRows =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box "monthly-close"; "p", box jobParams ]

        Assert.Equal(0L, progressRows)
    finally
        cleanupJobExecution jobParams
        cleanupLots ()
