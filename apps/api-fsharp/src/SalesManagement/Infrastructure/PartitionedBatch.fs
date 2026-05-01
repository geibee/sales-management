module SalesManagement.Infrastructure.PartitionedBatch

open System
open System.Diagnostics
open Npgsql

/// 1 パーティションが担当する id 範囲 (両端含む)。
type PartitionRange =
    { PartitionId: int
      FromId: int64
      ToId: int64 }

type PartitionStatus =
    | PartitionCompleted
    | PartitionSkipped
    | PartitionFailed of string

type PartitionResult =
    { PartitionId: int
      Read: int
      Processed: int
      ElapsedMilliseconds: int64
      Status: PartitionStatus }

type PartitioningOutcome =
    { Partitions: PartitionRange list
      Results: PartitionResult list
      TotalRead: int
      TotalProcessed: int
      ElapsedMilliseconds: int64 }

let private nowIso () = DateTime.UtcNow.ToString("o")

let private logPartitioning (jobName: string) (totalCount: int64) (parts: PartitionRange list) =
    let ranges =
        parts
        |> List.map (fun p -> sprintf "\"%d-%d\"" p.FromId p.ToId)
        |> String.concat ","

    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Partitioning\",\"job\":\"%s\",\"totalCount\":%d,\"partitions\":%d,\"ranges\":[%s]}"
        (nowIso ())
        jobName
        totalCount
        (List.length parts)
        ranges

let private logPartitionCompleted (jobName: string) (result: PartitionResult) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Partition %d completed\",\"job\":\"%s\",\"processed\":%d,\"elapsed\":\"%dms\"}"
        (nowIso ())
        result.PartitionId
        jobName
        result.Processed
        result.ElapsedMilliseconds

let private logPartitionSkipped (jobName: string) (partitionId: int) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Partition %d already completed, skipping\",\"job\":\"%s\"}"
        (nowIso ())
        partitionId
        jobName

let private logPartitionRestarting (jobName: string) (partitionId: int) (lastId: int64) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Partition %d restarting from last_processed_id=%d\",\"job\":\"%s\"}"
        (nowIso ())
        partitionId
        lastId
        jobName

let private logAllCompleted (jobName: string) (totalProcessed: int) (elapsed: int64) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"All partitions completed\",\"job\":\"%s\",\"totalProcessed\":%d,\"elapsed\":\"%dms\"}"
        (nowIso ())
        jobName
        totalProcessed
        elapsed

let private buildPartition (minId: int64) (maxId: int64) (rangeSize: int64) (count: int) (i: int) : PartitionRange =
    let fromId = minId + int64 i * rangeSize

    let toId = if i = count - 1 then maxId else fromId + rangeSize - 1L

    { PartitionId = i
      FromId = fromId
      ToId = toId }

let private buildSingletonPartition (minId: int64) (i: int) : PartitionRange =
    { PartitionId = i
      FromId = minId + int64 i
      ToId = minId + int64 i }

/// id 範囲 [minId, maxId] を `count` 個のパーティションに分割する。
/// 末尾のパーティションが余剰を吸収する (Step 6 実装ガイドの式に準拠)。
let computePartitions (minId: int64) (maxId: int64) (count: int) : PartitionRange list =
    if count <= 0 then
        invalidArg "count" "partition count must be positive"

    if minId > maxId then
        []
    else
        let total = maxId - minId + 1L
        let rangeSize = total / int64 count

        if rangeSize = 0L then
            // 件数 < パーティション数: 1 件 1 パーティションに割り、余剰は空にしない
            let n = int (min total (int64 count))
            [ for i in 0 .. n - 1 -> buildSingletonPartition minId i ]
        else
            [ for i in 0 .. count - 1 -> buildPartition minId maxId rangeSize count i ]

/// 指定の WHERE 条件にマッチする id の最小・最大を返す。レコードが無ければ None。
let queryMinMaxId (connectionString: string) (sql: string) : (int64 * int64) option =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)
    use rd = cmd.ExecuteReader()

    if rd.Read() && not (rd.IsDBNull 0) && not (rd.IsDBNull 1) then
        Some(rd.GetInt64(0), rd.GetInt64(1))
    else
        None

type PartitionedReader<'a> = NpgsqlConnection -> PartitionRange -> int64 -> int -> 'a list

let private chooseOk (processor: 'a -> Result<'b, string>) (item: 'a) : 'b option =
    match processor item with
    | Ok r -> Some r
    | Error _ -> None

type private LoopState =
    { mutable TotalRead: int
      mutable TotalProcessed: int
      mutable LastId: int64
      mutable HasMore: bool }

let private commitChunk
    (jobName: string)
    (jobParams: string)
    (partition: PartitionRange)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (conn: NpgsqlConnection)
    (lastIdOfChunk: int64)
    (totalAfter: int)
    (processed: 'b list)
    : unit =
    use tx = conn.BeginTransaction()
    writer tx processed

    ChunkProgressRepository.upsertPartitionProgress tx jobName jobParams partition.PartitionId lastIdOfChunk totalAfter

    tx.Commit()

let private processChunkOnce
    (jobName: string)
    (jobParams: string)
    (chunkSize: int)
    (partition: PartitionRange)
    (processor: 'a -> Result<'b, string>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    (state: LoopState)
    (conn: NpgsqlConnection)
    (chunk: 'a list)
    : unit =
    let processed = chunk |> List.choose (chooseOk processor)
    let lastIdOfChunk = chunk |> List.last |> getId
    let totalAfter = state.TotalProcessed + processed.Length
    commitChunk jobName jobParams partition writer conn lastIdOfChunk totalAfter processed
    state.TotalRead <- state.TotalRead + chunk.Length
    state.TotalProcessed <- totalAfter
    state.LastId <- lastIdOfChunk

    if chunk.Length < chunkSize then
        state.HasMore <- false

let private runPartitionChunkLoop
    (jobName: string)
    (jobParams: string)
    (connectionString: string)
    (chunkSize: int)
    (partition: PartitionRange)
    (startLastId: int64)
    (initialProcessedCount: int)
    (reader: PartitionedReader<'a>)
    (processor: 'a -> Result<'b, string>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : int * int * int64 =
    let state =
        { TotalRead = 0
          TotalProcessed = initialProcessedCount
          LastId = startLastId
          HasMore = true }

    while state.HasMore do
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        let chunk = reader conn partition state.LastId chunkSize

        if List.isEmpty chunk then
            state.HasMore <- false
        else
            processChunkOnce jobName jobParams chunkSize partition processor writer getId state conn chunk

    (state.TotalRead, state.TotalProcessed, state.LastId)

let private skippedResult (sw: Stopwatch) (partitionId: int) (processedCount: int) : PartitionResult =
    sw.Stop()

    { PartitionId = partitionId
      Read = 0
      Processed = processedCount
      ElapsedMilliseconds = sw.ElapsedMilliseconds
      Status = PartitionSkipped }

let private failedResult (sw: Stopwatch) (partitionId: int) (ex: exn) : PartitionResult =
    sw.Stop()

    { PartitionId = partitionId
      Read = 0
      Processed = 0
      ElapsedMilliseconds = sw.ElapsedMilliseconds
      Status = PartitionFailed ex.Message }

let private startState
    (jobName: string)
    (partition: PartitionRange)
    (progress: ChunkProgressRepository.PartitionProgress option)
    : int64 * int =
    match progress with
    | Some p ->
        logPartitionRestarting jobName partition.PartitionId p.LastProcessedId
        (p.LastProcessedId, p.ProcessedCount)
    | None -> (partition.FromId - 1L, 0)

let private executePartition
    (jobName: string)
    (jobParams: string)
    (connectionString: string)
    (chunkSize: int)
    (partition: PartitionRange)
    (sw: Stopwatch)
    (startLastId: int64)
    (initialProcessed: int)
    (reader: PartitionedReader<'a>)
    (processor: 'a -> Result<'b, string>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : PartitionResult =
    try
        let (read, processed, _lastId) =
            runPartitionChunkLoop
                jobName
                jobParams
                connectionString
                chunkSize
                partition
                startLastId
                initialProcessed
                reader
                processor
                writer
                getId

        ChunkProgressRepository.markPartitionCompleted
            connectionString
            jobName
            jobParams
            partition.PartitionId
            partition.ToId
            processed

        sw.Stop()

        let result =
            { PartitionId = partition.PartitionId
              Read = read
              Processed = processed
              ElapsedMilliseconds = sw.ElapsedMilliseconds
              Status = PartitionCompleted }

        logPartitionCompleted jobName result
        result
    with ex ->
        failedResult sw partition.PartitionId ex

let private processPartition
    (jobName: string)
    (jobParams: string)
    (connectionString: string)
    (chunkSize: int)
    (partition: PartitionRange)
    (reader: PartitionedReader<'a>)
    (processor: 'a -> Result<'b, string>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : PartitionResult =
    let sw = Stopwatch.StartNew()

    let progress =
        ChunkProgressRepository.tryGetPartitionProgress connectionString jobName jobParams partition.PartitionId

    match progress with
    | Some p when p.Status = "COMPLETED" ->
        logPartitionSkipped jobName partition.PartitionId
        skippedResult sw partition.PartitionId p.ProcessedCount
    | _ ->
        let (startLastId, initialProcessed) = startState jobName partition progress

        executePartition
            jobName
            jobParams
            connectionString
            chunkSize
            partition
            sw
            startLastId
            initialProcessed
            reader
            processor
            writer
            getId

/// 与えられたパーティション群を `Async.Parallel` で並列実行する。
/// 完了済みパーティション (status='COMPLETED') はスキップ、未完了は最後の進捗から再開する。
/// すべてのパーティションが成功した場合のみ progress 行を一括削除する。
let processInPartitions
    (jobName: string)
    (jobParams: string)
    (connectionString: string)
    (chunkSize: int)
    (partitions: PartitionRange list)
    (reader: PartitionedReader<'a>)
    (processor: 'a -> Result<'b, string>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : PartitioningOutcome =
    if chunkSize <= 0 then
        invalidArg "chunkSize" "chunkSize must be positive"

    let totalCount = partitions |> List.sumBy (fun p -> p.ToId - p.FromId + 1L)

    logPartitioning jobName totalCount partitions

    let sw = Stopwatch.StartNew()

    let results =
        partitions
        |> List.map (fun part -> async {
            return processPartition jobName jobParams connectionString chunkSize part reader processor writer getId
        })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.toList

    sw.Stop()

    let totalRead = results |> List.sumBy (fun r -> r.Read)
    let totalProcessed = results |> List.sumBy (fun r -> r.Processed)

    let allOk =
        results
        |> List.forall (fun r ->
            match r.Status with
            | PartitionCompleted
            | PartitionSkipped -> true
            | PartitionFailed _ -> false)

    if allOk then
        ChunkProgressRepository.deleteProgress connectionString jobName jobParams
        logAllCompleted jobName totalProcessed sw.ElapsedMilliseconds

    { Partitions = partitions
      Results = results
      TotalRead = totalRead
      TotalProcessed = totalProcessed
      ElapsedMilliseconds = sw.ElapsedMilliseconds }
