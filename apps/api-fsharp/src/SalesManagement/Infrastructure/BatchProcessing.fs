module SalesManagement.Infrastructure.BatchProcessing

open System
open System.Diagnostics
open Npgsql

type ChunkProgress =
    { ChunkIndex: int
      ProcessedInChunk: int
      TotalProcessed: int
      ElapsedMilliseconds: int64
      StartedFrom: int64 }

type BatchOutcome =
    { TotalRead: int
      TotalProcessed: int
      TotalSkipped: int
      ChunkCount: int }

type ChunkConfig =
    { MaxSkips: int
      MaxRetries: int
      IsRetryable: exn -> bool
      IsSkippable: exn -> bool }

let defaultChunkConfig: ChunkConfig =
    { MaxSkips = 0
      MaxRetries = 0
      IsRetryable = (fun _ -> false)
      IsSkippable = (fun _ -> false) }

type BatchListeners<'a> =
    { OnJobStart: string -> unit
      OnJobEnd: string -> unit
      OnChunkStart: int -> unit
      OnChunkEnd: int -> int -> int -> unit
      OnChunkError: int -> exn -> unit
      OnItemSkipped: 'a -> exn -> unit }

let defaultListeners<'a> : BatchListeners<'a> =
    { OnJobStart = ignore
      OnJobEnd = ignore
      OnChunkStart = ignore
      OnChunkEnd = (fun _ _ _ -> ())
      OnChunkError = (fun _ _ -> ())
      OnItemSkipped = (fun _ _ -> ()) }

exception SkipLimitExceededException of skipped: int

let private nowIso () = DateTime.UtcNow.ToString("o")

let private logRestart (jobName: string) (lastProcessedId: int64) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Restarting from last_processed_id=%d\",\"job\":\"%s\"}"
        (nowIso ())
        lastProcessedId
        jobName

let private logChunk (jobName: string) (p: ChunkProgress) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Chunk %d completed\",\"job\":\"%s\",\"processed\":%d,\"startedFrom\":%d,\"elapsed\":\"%dms\"}"
        (nowIso ())
        p.ChunkIndex
        jobName
        p.TotalProcessed
        p.StartedFrom
        p.ElapsedMilliseconds

let private logCompleted (jobName: string) (totalProcessed: int) (chunkCount: int) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Job completed\",\"job\":\"%s\",\"totalProcessed\":%d,\"chunks\":%d}"
        (nowIso ())
        jobName
        totalProcessed
        chunkCount

let private applyProcessor (processor: 'a -> Result<'b, 'err>) (chunk: 'a list) : 'b list =
    chunk
    |> List.choose (fun item ->
        match processor item with
        | Ok r -> Some r
        | Error _ -> None)

let private processSingleChunk
    (jobName: string)
    (jobParamsOpt: string option)
    (chunkIndex: int)
    (totalProcessedSoFar: int)
    (startedFrom: int64)
    (lastIdOfChunk: int64)
    (chunk: 'a list)
    (processor: 'a -> Result<'b, 'err>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (conn: NpgsqlConnection)
    : int =
    let sw = Stopwatch.StartNew()
    let processed = applyProcessor processor chunk
    use tx = conn.BeginTransaction()
    writer tx processed
    let total = totalProcessedSoFar + processed.Length

    match jobParamsOpt with
    | Some jp -> ChunkProgressRepository.upsertProgress tx jobName jp lastIdOfChunk total
    | None -> ()

    tx.Commit()
    sw.Stop()

    logChunk
        jobName
        { ChunkIndex = chunkIndex
          ProcessedInChunk = processed.Length
          TotalProcessed = total
          ElapsedMilliseconds = sw.ElapsedMilliseconds
          StartedFrom = startedFrom }

    processed.Length

let private resolveStartId (connectionString: string) (jobName: string) (jobParamsOpt: string option) : int64 =
    let lastId =
        match jobParamsOpt with
        | Some jp -> ChunkProgressRepository.getLastProcessedId connectionString jobName jp
        | None -> 0L

    if lastId > 0L then
        logRestart jobName lastId

    lastId

let private finalizeProgress (connectionString: string) (jobName: string) (jobParamsOpt: string option) : unit =
    match jobParamsOpt with
    | Some jp -> ChunkProgressRepository.deleteProgress connectionString jobName jp
    | None -> ()

type private ChunkLoopState =
    { mutable LastId: int64
      mutable ChunkIndex: int
      mutable TotalRead: int
      mutable TotalProcessed: int
      mutable TotalSkipped: int
      mutable HasMore: bool }

let private runChunkIteration
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (chunkSize: int)
    (reader: NpgsqlConnection -> int64 -> int -> 'a list)
    (processor: 'a -> Result<'b, 'err>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    (state: ChunkLoopState)
    : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    let chunk = reader conn state.LastId chunkSize

    if List.isEmpty chunk then
        state.HasMore <- false
    else
        state.ChunkIndex <- state.ChunkIndex + 1
        let startedFrom = state.LastId + 1L
        let lastIdOfChunk = chunk |> List.last |> getId

        let processedCount =
            processSingleChunk
                jobName
                jobParamsOpt
                state.ChunkIndex
                state.TotalProcessed
                startedFrom
                lastIdOfChunk
                chunk
                processor
                writer
                conn

        state.TotalRead <- state.TotalRead + chunk.Length
        state.TotalProcessed <- state.TotalProcessed + processedCount
        state.LastId <- lastIdOfChunk

        if chunk.Length < chunkSize then
            state.HasMore <- false

/// 汎用チャンク処理関数。
/// reader: (conn, lastId, limit) -> `lastId` より大きい id のレコードを最大 `chunkSize` 件返す
/// processor: ドメインロジックを適用する純粋関数
/// writer: (tran, results) -> 1チャンク分を書き込む。同一 TX 内で進捗 UPSERT も行われる
/// getId: 次回読み取りの基準となる連番
/// jobParamsOpt: Some の場合、batch_chunk_progress を使ったリスタートを有効化する
let processInChunks
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (chunkSize: int)
    (reader: NpgsqlConnection -> int64 -> int -> 'a list)
    (processor: 'a -> Result<'b, 'err>)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : BatchOutcome =
    if chunkSize <= 0 then
        invalidArg "chunkSize" "chunkSize must be positive"

    let state =
        { LastId = resolveStartId connectionString jobName jobParamsOpt
          ChunkIndex = 0
          TotalRead = 0
          TotalProcessed = 0
          TotalSkipped = 0
          HasMore = true }

    while state.HasMore do
        runChunkIteration jobName jobParamsOpt connectionString chunkSize reader processor writer getId state

    finalizeProgress connectionString jobName jobParamsOpt
    logCompleted jobName state.TotalProcessed state.ChunkIndex

    { TotalRead = state.TotalRead
      TotalProcessed = state.TotalProcessed
      TotalSkipped = state.TotalSkipped
      ChunkCount = state.ChunkIndex }

let private logSkipped (jobName: string) (itemId: int64) (ex: exn) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Warning\",\"message\":\"Item skipped\",\"job\":\"%s\",\"itemId\":%d,\"reason\":\"%s\"}"
        (nowIso ())
        jobName
        itemId
        (ex.Message.Replace("\"", "'"))

let private logRetryAttempt (jobName: string) (itemId: int64) (ex: exn) (attempt: int) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Warning\",\"message\":\"Retry attempt %d\",\"job\":\"%s\",\"itemId\":%d,\"error\":\"%s\"}"
        (nowIso ())
        attempt
        jobName
        itemId
        (ex.Message.Replace("\"", "'"))

let private logRetrySucceeded (jobName: string) (itemId: int64) (attempt: int) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Retry succeeded\",\"job\":\"%s\",\"itemId\":%d,\"attempt\":%d}"
        (nowIso ())
        jobName
        itemId
        attempt

let private processItemWithRetry
    (jobName: string)
    (config: ChunkConfig)
    (listeners: BatchListeners<'a>)
    (processor: 'a -> int -> 'b)
    (getId: 'a -> int64)
    (skipCounter: int ref)
    (item: 'a)
    : 'b option =
    let itemId = getId item
    let mutable attempt = 1
    let mutable result: 'b option = None
    let mutable finished = false

    while not finished do
        try
            let r = processor item attempt

            if attempt > 1 then
                logRetrySucceeded jobName itemId attempt

            result <- Some r
            finished <- true
        with ex ->
            if config.IsRetryable ex && attempt <= config.MaxRetries then
                logRetryAttempt jobName itemId ex attempt
                attempt <- attempt + 1
            elif config.IsSkippable ex then
                listeners.OnItemSkipped item ex
                logSkipped jobName itemId ex
                skipCounter.Value <- skipCounter.Value + 1

                if skipCounter.Value > config.MaxSkips then
                    raise (SkipLimitExceededException skipCounter.Value)

                finished <- true
            else
                reraise ()

    result

let private commitAdvancedChunk
    (jobName: string)
    (jobParamsOpt: string option)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (conn: NpgsqlConnection)
    (lastIdOfChunk: int64)
    (totalAfter: int)
    (processed: 'b list)
    : unit =
    use tx = conn.BeginTransaction()
    writer tx processed

    match jobParamsOpt with
    | Some jp -> ChunkProgressRepository.upsertProgress tx jobName jp lastIdOfChunk totalAfter
    | None -> ()

    tx.Commit()

let private executeAdvancedChunk
    (jobName: string)
    (jobParamsOpt: string option)
    (chunkSize: int)
    (config: ChunkConfig)
    (listeners: BatchListeners<'a>)
    (processor: 'a -> int -> 'b)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    (skipCounter: int ref)
    (state: ChunkLoopState)
    (conn: NpgsqlConnection)
    (chunk: 'a list)
    : unit =
    state.ChunkIndex <- state.ChunkIndex + 1
    let startedFrom = state.LastId + 1L
    let lastIdOfChunk = chunk |> List.last |> getId
    listeners.OnChunkStart state.ChunkIndex
    let skippedBefore = skipCounter.Value
    let sw = Stopwatch.StartNew()

    let processed =
        chunk
        |> List.choose (fun item -> processItemWithRetry jobName config listeners processor getId skipCounter item)

    let totalAfter = state.TotalProcessed + processed.Length
    commitAdvancedChunk jobName jobParamsOpt writer conn lastIdOfChunk totalAfter processed
    sw.Stop()
    let skippedInChunk = skipCounter.Value - skippedBefore

    logChunk
        jobName
        { ChunkIndex = state.ChunkIndex
          ProcessedInChunk = processed.Length
          TotalProcessed = totalAfter
          ElapsedMilliseconds = sw.ElapsedMilliseconds
          StartedFrom = startedFrom }

    listeners.OnChunkEnd state.ChunkIndex processed.Length skippedInChunk
    state.TotalRead <- state.TotalRead + chunk.Length
    state.TotalProcessed <- totalAfter
    state.TotalSkipped <- skipCounter.Value
    state.LastId <- lastIdOfChunk

    if chunk.Length < chunkSize then
        state.HasMore <- false

let private runAdvancedChunkIteration
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (chunkSize: int)
    (config: ChunkConfig)
    (listeners: BatchListeners<'a>)
    (reader: NpgsqlConnection -> int64 -> int -> 'a list)
    (processor: 'a -> int -> 'b)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    (skipCounter: int ref)
    (state: ChunkLoopState)
    : unit =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    let chunk = reader conn state.LastId chunkSize

    if List.isEmpty chunk then
        state.HasMore <- false
    else
        try
            executeAdvancedChunk
                jobName
                jobParamsOpt
                chunkSize
                config
                listeners
                processor
                writer
                getId
                skipCounter
                state
                conn
                chunk
        with ex ->
            listeners.OnChunkError state.ChunkIndex ex
            reraise ()

/// スキップ・リトライ・リスナー対応のチャンク処理関数。
/// processor: 例外スロー型。`attempt` には 1 起点の試行回数が渡される。
/// IsRetryable で true を返す例外は MaxRetries まで再試行、IsSkippable で true を返す例外は MaxSkips まで容認。
/// MaxSkips 超過で SkipLimitExceededException がスローされ、ジョブは失敗する。
let processInChunksWithConfig
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (chunkSize: int)
    (config: ChunkConfig)
    (listeners: BatchListeners<'a>)
    (reader: NpgsqlConnection -> int64 -> int -> 'a list)
    (processor: 'a -> int -> 'b)
    (writer: NpgsqlTransaction -> 'b list -> unit)
    (getId: 'a -> int64)
    : BatchOutcome =
    if chunkSize <= 0 then
        invalidArg "chunkSize" "chunkSize must be positive"

    listeners.OnJobStart jobName

    let state =
        { LastId = resolveStartId connectionString jobName jobParamsOpt
          ChunkIndex = 0
          TotalRead = 0
          TotalProcessed = 0
          TotalSkipped = 0
          HasMore = true }

    let skipCounter = ref 0

    while state.HasMore do
        runAdvancedChunkIteration
            jobName
            jobParamsOpt
            connectionString
            chunkSize
            config
            listeners
            reader
            processor
            writer
            getId
            skipCounter
            state

    finalizeProgress connectionString jobName jobParamsOpt
    logCompleted jobName state.TotalProcessed state.ChunkIndex
    listeners.OnJobEnd jobName

    { TotalRead = state.TotalRead
      TotalProcessed = state.TotalProcessed
      TotalSkipped = state.TotalSkipped
      ChunkCount = state.ChunkIndex }
