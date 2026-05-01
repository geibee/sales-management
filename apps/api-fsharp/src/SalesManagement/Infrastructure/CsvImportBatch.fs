module SalesManagement.Infrastructure.CsvImportBatch

open System
open System.Diagnostics
open System.IO
open System.Text
open Npgsql

type LotImportRow =
    { LineNumber: int64
      LotNumberYear: int
      LotNumberLocation: string
      LotNumberSeq: int
      DivisionCode: int
      DepartmentCode: int
      SectionCode: int
      ProcessCategory: int
      InspectionCategory: int
      ManufacturingCategory: int }

type CsvSourceRow = { LineNumber: int64; Raw: string }

type ImportOutcome =
    { TotalRead: int
      TotalWritten: int
      TotalSkipped: int
      ChunkCount: int }

type JobRunOutcome =
    | Completed of ImportOutcome
    | AlreadyRunning
    | AlreadyCompleted

let private nowIso () = DateTime.UtcNow.ToString("o")

let private escape (s: string) =
    s.Replace("\\", "\\\\").Replace("\"", "'")

let private logRowSkipped (jobName: string) (line: int64) (reason: string) (raw: string) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Warning\",\"message\":\"Row skipped\",\"job\":\"%s\",\"line\":%d,\"reason\":\"%s\",\"raw\":\"%s\"}"
        (nowIso ())
        jobName
        line
        (escape reason)
        (escape raw)

let private logChunk (jobName: string) (chunkIndex: int) (processed: int) (total: int) (elapsedMs: int64) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Chunk %d completed\",\"job\":\"%s\",\"processed\":%d,\"total\":%d,\"elapsed\":\"%dms\"}"
        (nowIso ())
        chunkIndex
        jobName
        processed
        total
        elapsedMs

let private logCompleted (jobName: string) (outcome: ImportOutcome) =
    printfn
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Job completed\",\"job\":\"%s\",\"read\":%d,\"written\":%d,\"skipped\":%d,\"chunks\":%d}"
        (nowIso ())
        jobName
        outcome.TotalRead
        outcome.TotalWritten
        outcome.TotalSkipped
        outcome.ChunkCount

let mutable private codePagesRegistered = false

let private normalizeEncodingName (name: string) : string =
    match name.Trim().ToLowerInvariant() with
    | "windows-31j"
    | "cp932"
    | "ms932"
    | "ms_kanji" -> "shift_jis"
    | other -> other

let resolveEncoding (name: string) : Encoding =
    if not codePagesRegistered then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        codePagesRegistered <- true

    if String.IsNullOrEmpty name then
        Encoding.UTF8
    else
        Encoding.GetEncoding(normalizeEncodingName name)

/// CSV ファイルをストリーム読み取りする。先頭行はヘッダとしてスキップする。
/// 返却される LineNumber は「ヘッダを除いたデータ行の連番 (1 起点)」。
let readCsvSource (filePath: string) (encoding: Encoding) : CsvSourceRow seq = seq {
    use reader = new StreamReader(filePath, encoding)
    let mutable dataLine = 0L
    let mutable headerSkipped = false

    while not reader.EndOfStream do
        let line = reader.ReadLine()

        if not (String.IsNullOrWhiteSpace line) then
            if not headerSkipped then
                headerSkipped <- true
            else
                dataLine <- dataLine + 1L
                yield { LineNumber = dataLine; Raw = line }
}

let private tryParsePositive (s: string) (field: string) : Result<int, string> =
    match Int32.TryParse(s.Trim()) with
    | true, v when v > 0 -> Ok v
    | _ -> Error(sprintf "%s must be a positive integer" field)

let private parseAllPositive (fields: (string * string) array) : Result<int array, string> =
    let mutable err: string option = None
    let values = ResizeArray<int>()

    for (name, value) in fields do
        match err, tryParsePositive value name with
        | Some _, _ -> ()
        | None, Ok v -> values.Add v
        | None, Error e -> err <- Some e

    match err with
    | Some e -> Error e
    | None -> Ok(values.ToArray())

let private buildRow (lineNumber: int64) (location: string) (values: int array) : LotImportRow =
    { LineNumber = lineNumber
      LotNumberYear = values.[0]
      LotNumberLocation = location
      LotNumberSeq = values.[1]
      DivisionCode = values.[2]
      DepartmentCode = values.[3]
      SectionCode = values.[4]
      ProcessCategory = values.[5]
      InspectionCategory = values.[6]
      ManufacturingCategory = values.[7] }

let parseLine (lineNumber: int64) (raw: string) : Result<LotImportRow, string> =
    let parts = raw.Split(',')

    if parts.Length < 9 then
        Error "expected 9 columns"
    else
        let location = parts.[1].Trim()

        if String.IsNullOrWhiteSpace location then
            Error "lot_number_location must not be empty"
        else
            let fields =
                [| "lot_number_year", parts.[0]
                   "lot_number_seq", parts.[2]
                   "division_code", parts.[3]
                   "department_code", parts.[4]
                   "section_code", parts.[5]
                   "process_category", parts.[6]
                   "inspection_category", parts.[7]
                   "manufacturing_category", parts.[8] |]

            parseAllPositive fields |> Result.map (buildRow lineNumber location)

let private insertRow (tx: NpgsqlTransaction) (row: LotImportRow) : int =
    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO lot (lot_number_year, lot_number_location, lot_number_seq,
                             division_code, department_code, section_code,
                             process_category, inspection_category, manufacturing_category,
                             status)
            VALUES (@y, @l, @s, @d, @dp, @sc, @p, @i, @m, 'manufacturing')
            ON CONFLICT (lot_number_year, lot_number_location, lot_number_seq) DO NOTHING
            """,
            tx.Connection,
            tx
        )

    cmd.Parameters.AddWithValue("y", row.LotNumberYear) |> ignore
    cmd.Parameters.AddWithValue("l", row.LotNumberLocation) |> ignore
    cmd.Parameters.AddWithValue("s", row.LotNumberSeq) |> ignore
    cmd.Parameters.AddWithValue("d", row.DivisionCode) |> ignore
    cmd.Parameters.AddWithValue("dp", row.DepartmentCode) |> ignore
    cmd.Parameters.AddWithValue("sc", row.SectionCode) |> ignore
    cmd.Parameters.AddWithValue("p", row.ProcessCategory) |> ignore
    cmd.Parameters.AddWithValue("i", row.InspectionCategory) |> ignore
    cmd.Parameters.AddWithValue("m", row.ManufacturingCategory) |> ignore
    cmd.ExecuteNonQuery()

let private writeChunk (tx: NpgsqlTransaction) (rows: LotImportRow list) : unit =
    for row in rows do
        insertRow tx row |> ignore

type private RunState =
    { mutable LastLineNumber: int64
      mutable ChunkIndex: int
      mutable TotalRead: int
      mutable TotalWritten: int
      mutable TotalSkipped: int
      Buffer: ResizeArray<LotImportRow> }

let private flushBuffer
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (state: RunState)
    : unit =
    if state.Buffer.Count > 0 then
        let sw = Stopwatch.StartNew()
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        use tx = conn.BeginTransaction()
        let rows = state.Buffer |> List.ofSeq
        writeChunk tx rows
        let writtenAfter = state.TotalWritten + rows.Length

        match jobParamsOpt with
        | Some jp -> ChunkProgressRepository.upsertProgress tx jobName jp state.LastLineNumber writtenAfter
        | None -> ()

        tx.Commit()
        sw.Stop()
        state.ChunkIndex <- state.ChunkIndex + 1
        state.TotalWritten <- writtenAfter
        logChunk jobName state.ChunkIndex rows.Length writtenAfter sw.ElapsedMilliseconds
        state.Buffer.Clear()

let private runCore
    (jobName: string)
    (jobParamsOpt: string option)
    (connectionString: string)
    (chunkSize: int)
    (filePath: string)
    (encoding: Encoding)
    : ImportOutcome =
    if chunkSize <= 0 then
        invalidArg "chunkSize" "chunkSize must be positive"

    let startId =
        match jobParamsOpt with
        | Some jp -> ChunkProgressRepository.getLastProcessedId connectionString jobName jp
        | None -> 0L

    let state =
        { LastLineNumber = startId
          ChunkIndex = 0
          TotalRead = 0
          TotalWritten = 0
          TotalSkipped = 0
          Buffer = ResizeArray() }

    for source in readCsvSource filePath encoding do
        if source.LineNumber > startId then
            state.TotalRead <- state.TotalRead + 1

            match parseLine source.LineNumber source.Raw with
            | Ok row ->
                state.Buffer.Add row
                state.LastLineNumber <- source.LineNumber

                if state.Buffer.Count >= chunkSize then
                    flushBuffer jobName jobParamsOpt connectionString state
            | Error reason ->
                state.TotalSkipped <- state.TotalSkipped + 1
                state.LastLineNumber <- source.LineNumber
                logRowSkipped jobName source.LineNumber reason source.Raw

    flushBuffer jobName jobParamsOpt connectionString state

    match jobParamsOpt with
    | Some jp -> ChunkProgressRepository.deleteProgress connectionString jobName jp
    | None -> ()

    let outcome =
        { TotalRead = state.TotalRead
          TotalWritten = state.TotalWritten
          TotalSkipped = state.TotalSkipped
          ChunkCount = state.ChunkIndex }

    logCompleted jobName outcome
    outcome

/// CSV インポートバッチ: 行をバリデーションし、正常行のみ chunkSize 単位で lot テーブルへ INSERT する。
/// この経路ではリスタート (batch_chunk_progress) を有効化しない。
let runImportLots (connectionString: string) (chunkSize: int) (filePath: string) (encoding: Encoding) : ImportOutcome =
    runCore "import-lots" None connectionString chunkSize filePath encoding

/// CSV インポートバッチ + 二重実行防止 + 完了/失敗ステータス記録 + チャンクリスタート。
/// jobParams にはファイルパスを使い、batch_job_execution / batch_chunk_progress に進捗を記録する。
let runImportLotsManaged
    (connectionString: string)
    (chunkSize: int)
    (jobName: string)
    (filePath: string)
    (encoding: Encoding)
    : JobRunOutcome =
    let jobParams = filePath

    match JobExecutionRepository.tryStart connectionString jobName jobParams with
    | JobExecutionRepository.AlreadyRunning -> AlreadyRunning
    | JobExecutionRepository.AlreadyCompleted -> AlreadyCompleted
    | JobExecutionRepository.Started ->
        try
            let outcome =
                runCore jobName (Some jobParams) connectionString chunkSize filePath encoding

            JobExecutionRepository.complete
                connectionString
                jobName
                jobParams
                outcome.TotalRead
                outcome.TotalWritten
                outcome.TotalSkipped

            Completed outcome
        with ex ->
            JobExecutionRepository.fail connectionString jobName jobParams ex.Message
            reraise ()
