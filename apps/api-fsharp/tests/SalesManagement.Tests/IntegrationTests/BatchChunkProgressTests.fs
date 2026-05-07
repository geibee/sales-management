module SalesManagement.Tests.IntegrationTests.BatchChunkProgressTests

open Npgsql
open Xunit
open SalesManagement.Infrastructure
open SalesManagement.Tests.Support.BatchFixture

let private testYear = 2097
let private testLocation = "T4"

let private cleanupLotsLocal () = cleanupLots testYear testLocation
let private cleanupJobExecution (jobParams: string) = cleanupMonthlyCloseJob jobParams
let private seedManufacturedLotsLocal (count: int) =
    seedManufacturedLots testYear testLocation count "2097-04-01"

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``batch_chunk_progress is deleted after successful run`` () =
    let jobParams = sprintf "%d-04" testYear

    try
        seedManufacturedLotsLocal 25

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
        cleanupLotsLocal ()

[<Fact>]
[<Trait("Category", "BatchChunkProgress")>]
let ``restart from FAILED with chunk_progress skips already-processed lots`` () =
    let jobParams = sprintf "%d-05" testYear

    try
        seedManufacturedLotsLocal 50

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
        cleanupLotsLocal ()

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
        seedManufacturedLotsLocal 30

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
        cleanupLotsLocal ()
