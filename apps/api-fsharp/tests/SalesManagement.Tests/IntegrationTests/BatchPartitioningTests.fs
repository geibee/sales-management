module SalesManagement.Tests.IntegrationTests.BatchPartitioningTests

open System
open System.Threading
open Npgsql
open Xunit
open SalesManagement.Infrastructure
open SalesManagement.Infrastructure.PartitionedBatch

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

let private testYear = 2095
let private testLocation = "T6"

let private cleanupLots () =
    execParam
        "DELETE FROM lot_detail WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

    execParam
        "DELETE FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

let private cleanupJobExecution (jobName: string) (jobParams: string) =
    execParam
        "DELETE FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
        [ "n", box jobName; "p", box jobParams ]

    execParam
        "DELETE FROM batch_job_execution WHERE job_name = @n AND job_params = @p"
        [ "n", box jobName; "p", box jobParams ]

let private ensureJobExecution (jobName: string) (jobParams: string) =
    execParam
        """
        INSERT INTO batch_job_execution (job_name, job_params, status)
        VALUES (@n, @p, 'RUNNING')
        ON CONFLICT (job_name, job_params) DO NOTHING
        """
        [ "n", box jobName; "p", box jobParams ]

let private seedLots (count: int) : int64 list =
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
                   'manufactured', '2095-04-01'
              FROM generate_series(1, @c) AS seq
            RETURNING id
            """,
            conn,
            tx
        )

    cmd.Parameters.AddWithValue("y", testYear) |> ignore
    cmd.Parameters.AddWithValue("loc", testLocation) |> ignore
    cmd.Parameters.AddWithValue("c", count) |> ignore
    use rd = cmd.ExecuteReader()

    let ids =
        [ while rd.Read() do
              yield rd.GetInt64(0) ]

    rd.Close()
    tx.Commit()
    ids

type private TestRow = { Id: int64; Seq: int }

let private partitionReader
    (conn: NpgsqlConnection)
    (part: PartitionRange)
    (lastId: int64)
    (limit: int)
    : TestRow list =
    use cmd =
        new NpgsqlCommand(
            """
            SELECT id, lot_number_seq
              FROM lot
             WHERE lot_number_year = @y AND lot_number_location = @loc
               AND status = 'manufactured'
               AND id > @last_id AND id <= @to_id AND id >= @from_id
             ORDER BY id
             LIMIT @lim
            """,
            conn
        )

    cmd.Parameters.AddWithValue("y", testYear) |> ignore
    cmd.Parameters.AddWithValue("loc", testLocation) |> ignore
    cmd.Parameters.AddWithValue("last_id", lastId) |> ignore
    cmd.Parameters.AddWithValue("from_id", part.FromId) |> ignore
    cmd.Parameters.AddWithValue("to_id", part.ToId) |> ignore
    cmd.Parameters.AddWithValue("lim", limit) |> ignore
    use rd = cmd.ExecuteReader()

    [ while rd.Read() do
          yield
              { Id = rd.GetInt64(0)
                Seq = rd.GetInt32(1) } ]

let private writer (tx: NpgsqlTransaction) (rows: int64 list) : unit =
    for id in rows do
        use cmd =
            new NpgsqlCommand(
                """
                UPDATE lot SET status = 'shipping_instructed'
                 WHERE id = @id AND status = 'manufactured'
                """,
                tx.Connection,
                tx
            )

        cmd.Parameters.AddWithValue("id", id) |> ignore
        cmd.ExecuteNonQuery() |> ignore

[<Fact>]
[<Trait("Category", "BatchPartitioning")>]
let ``computePartitions splits range into N contiguous non-overlapping parts`` () =
    let parts = computePartitions 1L 100L 5

    Assert.Equal(5, List.length parts)
    Assert.Equal(1L, parts.[0].FromId)
    Assert.Equal(100L, (List.last parts).ToId)

    // 連続性: 隣接パーティションの境界が連続している
    parts
    |> List.pairwise
    |> List.iter (fun (a, b) -> Assert.Equal(a.ToId + 1L, b.FromId))

    // PartitionId が 0..N-1
    parts |> List.iteri (fun i p -> Assert.Equal(i, p.PartitionId))

[<Fact>]
[<Trait("Category", "BatchPartitioning")>]
let ``computePartitions absorbs remainder in the last partition`` () =
    let parts = computePartitions 1L 103L 5
    Assert.Equal(5, List.length parts)

    // 最初の 4 パーティションは 20 件ずつ、最後は 23 件
    Assert.Equal(20L, parts.[0].ToId - parts.[0].FromId + 1L)
    Assert.Equal(23L, parts.[4].ToId - parts.[4].FromId + 1L)

[<Fact>]
[<Trait("Category", "BatchPartitioning")>]
let ``parallel partitioned processing processes all items exactly once`` () =
    let jobName = sprintf "parallel-test-%s" (Guid.NewGuid().ToString("N"))
    let jobParams = "2026-04"

    try
        let ids = seedLots 50
        let minId = List.head ids
        let maxId = List.last ids
        let parts = computePartitions minId maxId 5
        ensureJobExecution jobName jobParams

        let processor (row: TestRow) : Result<int64, string> = Ok row.Id

        let outcome =
            processInPartitions jobName jobParams connectionString 10 parts partitionReader processor writer (fun r ->
                r.Id)

        Assert.Equal(5, List.length outcome.Results)
        Assert.Equal(50, outcome.TotalProcessed)

        // 全件 shipping_instructed に更新されていること
        let shipped =
            queryScalarInt
                """
                SELECT count(*) FROM lot
                 WHERE lot_number_year = @y AND lot_number_location = @loc
                   AND status = 'shipping_instructed'
                """
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(50L, shipped)

        // 全パーティション完了後に進捗行は削除される
        let progressRows =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box jobName; "p", box jobParams ]

        Assert.Equal(0L, progressRows)
    finally
        cleanupJobExecution jobName jobParams
        cleanupLots ()

[<Fact>]
[<Trait("Category", "BatchPartitioning")>]
let ``partition restart skips completed partitions and resumes failed one`` () =
    let jobName = sprintf "partition-restart-%s" (Guid.NewGuid().ToString("N"))
    let jobParams = "2026-04"

    try
        let ids = seedLots 50
        let minId = List.head ids
        let maxId = List.last ids
        let parts = computePartitions minId maxId 5
        ensureJobExecution jobName jobParams

        // 1 回目: パーティション 2 だけわざと失敗させる
        let failPartition2Processor (row: TestRow) : Result<int64, string> =
            // この processor は呼び出されたパーティションを直接知らないため、id 範囲で判定する
            if row.Id >= parts.[2].FromId && row.Id <= parts.[2].ToId then
                failwith "intentional failure on partition 2"
            else
                Ok row.Id

        // パーティション 2 の中身を 1 件以上処理させて last_processed_id を進めるため、
        // 「最初の数件は成功、その後で失敗」する processor を仕込む
        let partition2Processed = ref 0

        let throwingProcessor (row: TestRow) : Result<int64, string> =
            if row.Id >= parts.[2].FromId && row.Id <= parts.[2].ToId then
                let n = Interlocked.Increment(partition2Processed)

                if n > 3 then
                    failwith "intentional failure on partition 2"
                else
                    Ok row.Id
            else
                Ok row.Id

        let firstOutcome =
            processInPartitions
                jobName
                jobParams
                connectionString
                2 // 小さなチャンクで進捗を残しやすくする
                parts
                partitionReader
                throwingProcessor
                writer
                (fun r -> r.Id)

        // パーティション 2 は FAILED、それ以外は COMPLETED
        let failedCount =
            firstOutcome.Results
            |> List.filter (fun r ->
                match r.Status with
                | PartitionFailed _ -> true
                | _ -> false)
            |> List.length

        Assert.Equal(1, failedCount)

        // 失敗したパーティションがあるので進捗行は残る
        let progressBefore =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box jobName; "p", box jobParams ]

        Assert.True(progressBefore > 0L, "progress rows should remain after partial failure")

        // 完了パーティション (status = 'COMPLETED') が 4 つあること
        let completedRows =
            queryScalarInt
                """
                SELECT count(*) FROM batch_chunk_progress
                 WHERE job_name = @n AND job_params = @p AND status = 'COMPLETED'
                """
                [ "n", box jobName; "p", box jobParams ]

        Assert.Equal(4L, completedRows)

        // パーティション 2 の進捗行が RUNNING で残っていること
        let p2Status =
            ChunkProgressRepository.tryGetPartitionProgress connectionString jobName jobParams 2

        match p2Status with
        | Some p -> Assert.Equal("RUNNING", p.Status)
        | None -> Assert.Fail("partition 2 progress row should exist")

        // 2 回目: 失敗を解消した processor で再実行
        let successProcessor (row: TestRow) : Result<int64, string> = Ok row.Id

        let secondOutcome =
            processInPartitions
                jobName
                jobParams
                connectionString
                2
                parts
                partitionReader
                successProcessor
                writer
                (fun r -> r.Id)

        // パーティション 0,1,3,4 は SKIPPED、パーティション 2 のみ COMPLETED となる
        let skipped =
            secondOutcome.Results
            |> List.filter (fun r ->
                match r.Status with
                | PartitionSkipped -> true
                | _ -> false)
            |> List.length

        let completed =
            secondOutcome.Results
            |> List.filter (fun r ->
                match r.Status with
                | PartitionCompleted -> true
                | _ -> false)
            |> List.length

        Assert.Equal(4, skipped)
        Assert.Equal(1, completed)

        // 全件 shipping_instructed に更新されていること
        let shipped =
            queryScalarInt
                """
                SELECT count(*) FROM lot
                 WHERE lot_number_year = @y AND lot_number_location = @loc
                   AND status = 'shipping_instructed'
                """
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(50L, shipped)

        // 進捗行は全削除されている
        let progressAfter =
            queryScalarInt
                "SELECT count(*) FROM batch_chunk_progress WHERE job_name = @n AND job_params = @p"
                [ "n", box jobName; "p", box jobParams ]

        Assert.Equal(0L, progressAfter)
    finally
        cleanupJobExecution jobName jobParams
        cleanupLots ()
