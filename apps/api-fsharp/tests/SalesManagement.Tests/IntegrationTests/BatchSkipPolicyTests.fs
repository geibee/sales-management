module SalesManagement.Tests.IntegrationTests.BatchSkipPolicyTests

open System
open System.Threading
open Npgsql
open Xunit
open SalesManagement.Infrastructure
open SalesManagement.Infrastructure.BatchProcessing
open SalesManagement.Tests.Support.BatchFixture

let private testYear = 2096
let private testLocation = "T5"

let private cleanupLotsLocal () = cleanupLots testYear testLocation

let private seedLots (count: int) =
    seedManufacturedLots testYear testLocation count "2096-04-01"

type private TestRow = { Id: int64; Seq: int }

let private reader (conn: NpgsqlConnection) (lastId: int64) (limit: int) : TestRow list =
    use cmd =
        new NpgsqlCommand(
            """
            SELECT id, lot_number_seq
              FROM lot
             WHERE lot_number_year = @y AND lot_number_location = @loc
               AND status = 'manufactured'
               AND id > @last_id
             ORDER BY id
             LIMIT @lim
            """,
            conn
        )

    cmd.Parameters.AddWithValue("y", testYear) |> ignore
    cmd.Parameters.AddWithValue("loc", testLocation) |> ignore
    cmd.Parameters.AddWithValue("last_id", lastId) |> ignore
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

exception ValidationException of string
exception TransientException of string

[<Fact>]
[<Trait("Category", "BatchSkipPolicy")>]
let ``items raising skippable exception are skipped within MaxSkips`` () =
    let jobName = sprintf "skip-test-%s" (Guid.NewGuid().ToString("N"))

    try
        seedLots 30
        let invalidSeqs = Set.ofList [ 5; 10; 15; 20; 25 ]

        let processor (row: TestRow) (_attempt: int) : int64 =
            if Set.contains row.Seq invalidSeqs then
                raise (ValidationException(sprintf "Invalid status for seq=%d" row.Seq))
            else
                row.Id

        let config =
            { defaultChunkConfig with
                MaxSkips = 10
                IsSkippable = (fun ex -> ex :? ValidationException) }

        let outcome =
            processInChunksWithConfig
                jobName
                None
                connectionString
                10
                config
                defaultListeners
                reader
                processor
                writer
                (fun r -> r.Id)

        Assert.Equal(30, outcome.TotalRead)
        Assert.Equal(25, outcome.TotalProcessed)
        Assert.Equal(5, outcome.TotalSkipped)
    finally
        cleanupLotsLocal ()

[<Fact>]
[<Trait("Category", "BatchSkipPolicy")>]
let ``exceeding MaxSkips raises SkipLimitExceededException`` () =
    let jobName = sprintf "skip-limit-%s" (Guid.NewGuid().ToString("N"))

    try
        seedLots 30
        let invalidSeqs = Set.ofList [ 1; 2; 3; 4; 5; 6 ]

        let processor (row: TestRow) (_attempt: int) : int64 =
            if Set.contains row.Seq invalidSeqs then
                raise (ValidationException(sprintf "Invalid seq=%d" row.Seq))
            else
                row.Id

        let config =
            { defaultChunkConfig with
                MaxSkips = 3
                IsSkippable = (fun ex -> ex :? ValidationException) }

        let act () =
            processInChunksWithConfig
                jobName
                None
                connectionString
                10
                config
                defaultListeners
                reader
                processor
                writer
                (fun r -> r.Id)
            |> ignore

        Assert.Throws<SkipLimitExceededException>(act) |> ignore
    finally
        cleanupLotsLocal ()

[<Fact>]
[<Trait("Category", "BatchSkipPolicy")>]
let ``retryable exception on first attempt succeeds on second attempt`` () =
    let jobName = sprintf "retry-test-%s" (Guid.NewGuid().ToString("N"))

    try
        seedLots 10
        let retryTargetSeq = 7
        let attempts = System.Collections.Concurrent.ConcurrentDictionary<int, int>()

        let processor (row: TestRow) (attempt: int) : int64 =
            attempts.[row.Seq] <- attempt

            if row.Seq = retryTargetSeq && attempt = 1 then
                raise (TransientException "Transient error (test)")
            else
                row.Id

        let config =
            { defaultChunkConfig with
                MaxRetries = 3
                IsRetryable = (fun ex -> ex :? TransientException) }

        let outcome =
            processInChunksWithConfig
                jobName
                None
                connectionString
                10
                config
                defaultListeners
                reader
                processor
                writer
                (fun r -> r.Id)

        Assert.Equal(10, outcome.TotalRead)
        Assert.Equal(10, outcome.TotalProcessed)
        Assert.Equal(0, outcome.TotalSkipped)
        Assert.Equal(2, attempts.[retryTargetSeq])
    finally
        cleanupLotsLocal ()

[<Fact>]
[<Trait("Category", "BatchSkipPolicy")>]
let ``listeners receive job and chunk lifecycle callbacks`` () =
    let jobName = sprintf "listener-test-%s" (Guid.NewGuid().ToString("N"))

    try
        seedLots 25
        let invalidSeqs = Set.ofList [ 4; 12 ]

        let jobStarts = ref 0
        let jobEnds = ref 0
        let chunkStarts = ResizeArray<int>()
        let chunkEnds = ResizeArray<int * int * int>()
        let itemsSkipped = ref 0

        let processor (row: TestRow) (_attempt: int) : int64 =
            if Set.contains row.Seq invalidSeqs then
                raise (ValidationException "skip me")
            else
                row.Id

        let listeners: BatchListeners<TestRow> =
            { OnJobStart = (fun _ -> Interlocked.Increment(jobStarts) |> ignore)
              OnJobEnd = (fun _ -> Interlocked.Increment(jobEnds) |> ignore)
              OnChunkStart = (fun i -> lock chunkStarts (fun () -> chunkStarts.Add i))
              OnChunkEnd = (fun i p s -> lock chunkEnds (fun () -> chunkEnds.Add(i, p, s)))
              OnChunkError = (fun _ _ -> ())
              OnItemSkipped = (fun _ _ -> Interlocked.Increment(itemsSkipped) |> ignore) }

        let config =
            { defaultChunkConfig with
                MaxSkips = 10
                IsSkippable = (fun ex -> ex :? ValidationException) }

        let outcome =
            processInChunksWithConfig
                jobName
                None
                connectionString
                10
                config
                listeners
                reader
                processor
                writer
                (fun r -> r.Id)

        Assert.Equal(1, jobStarts.Value)
        Assert.Equal(1, jobEnds.Value)
        Assert.Equal(3, chunkStarts.Count) // 25 items / chunk 10 → 3 chunks
        Assert.Equal(3, chunkEnds.Count)
        Assert.Equal(2, itemsSkipped.Value)

        let totalProcessed = chunkEnds |> Seq.sumBy (fun (_, p, _) -> p)
        let totalSkippedFromChunks = chunkEnds |> Seq.sumBy (fun (_, _, s) -> s)
        Assert.Equal(23, totalProcessed)
        Assert.Equal(2, totalSkippedFromChunks)
        Assert.Equal(23, outcome.TotalProcessed)
        Assert.Equal(2, outcome.TotalSkipped)
    finally
        cleanupLotsLocal ()
