module SalesManagement.BatchRunner.Program

open System
open SalesManagement.Infrastructure

let private parseArg (prefix: string) (args: string array) : string option =
    args
    |> Array.tryPick (fun a ->
        if a.StartsWith(prefix + "=") then
            Some(a.Substring(prefix.Length + 1))
        else
            None)

let private resolveConnectionString () : string =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

let private resolveChunkSize () : int =
    match Environment.GetEnvironmentVariable("CHUNK_SIZE") with
    | null
    | "" -> 1000
    | s ->
        match Int32.TryParse s with
        | true, n when n > 0 -> n
        | _ -> 1000

let private resolveImportJobName (job: string option) : string =
    match job with
    | Some j -> j
    | None -> "import-lots"

let private runImportLots (args: string array) : int =
    match parseArg "--file" args with
    | None ->
        eprintfn "usage: BatchRunner --job=import-lots --file=<path> [--encoding=<name>]"
        2
    | Some filePath ->
        let encodingName = parseArg "--encoding" args |> Option.defaultValue "utf-8"

        let encoding = CsvImportBatch.resolveEncoding encodingName
        let cs = resolveConnectionString ()
        let chunk = resolveChunkSize ()
        let jobName = resolveImportJobName (parseArg "--job" args)

        try
            match CsvImportBatch.runImportLotsManaged cs chunk jobName filePath encoding with
            | CsvImportBatch.Completed outcome ->
                printfn
                    "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Batch finished\",\"job\":\"%s\",\"file\":\"%s\",\"chunkSize\":%d,\"chunks\":%d,\"read\":%d,\"written\":%d,\"skipped\":%d}"
                    (DateTime.UtcNow.ToString("o"))
                    jobName
                    filePath
                    chunk
                    outcome.ChunkCount
                    outcome.TotalRead
                    outcome.TotalWritten
                    outcome.TotalSkipped

                0
            | CsvImportBatch.AlreadyRunning ->
                printfn "Job '%s' with params '%s' is already running" jobName filePath
                0
            | CsvImportBatch.AlreadyCompleted ->
                printfn "Job '%s' with params '%s' already completed" jobName filePath
                0
        with ex ->
            eprintfn "Batch failed: %s" ex.Message
            1

let run (args: string array) : int =
    let job = parseArg "--job" args
    let date = parseArg "--date" args

    let isImportLots =
        match job with
        | Some name -> name.StartsWith("import-lots")
        | None -> false

    match job, date with
    | _ when isImportLots -> runImportLots args
    | None, _ ->
        eprintfn "usage: BatchRunner --job=<name> --date=<YYYY-MM> | --job=import-lots --file=<path>"
        2
    | Some "monthly-close", Some d ->
        let cs = resolveConnectionString ()
        let chunk = resolveChunkSize ()

        try
            match MonthlyCloseBatch.runMonthlyCloseManaged cs chunk d with
            | MonthlyCloseBatch.Completed outcome ->
                printfn
                    "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Batch finished\",\"job\":\"monthly-close\",\"params\":\"%s\",\"chunkSize\":%d,\"chunks\":%d,\"totalRead\":%d,\"totalProcessed\":%d}"
                    (DateTime.UtcNow.ToString("o"))
                    d
                    chunk
                    outcome.ChunkCount
                    outcome.TotalRead
                    outcome.TotalProcessed

                0
            | MonthlyCloseBatch.AlreadyRunning ->
                printfn "Job 'monthly-close' with params '%s' is already running" d
                0
            | MonthlyCloseBatch.AlreadyCompleted ->
                printfn "Job 'monthly-close' with params '%s' already completed" d
                0
        with ex ->
            eprintfn "Batch failed: %s" ex.Message
            1
    | Some("monitoring-test" as name), Some d
    | Some("e2e-test" as name), Some d ->
        let cs = resolveConnectionString ()
        let config = CloudWatchPublisher.resolveConfig ()
        let jobParams = d

        let processed =
            match Environment.GetEnvironmentVariable("MONITORING_PROCESSED_COUNT") with
            | null
            | "" -> 10000
            | s ->
                match Int32.TryParse s with
                | true, n when n >= 0 -> n
                | _ -> 10000

        let skipped =
            match Environment.GetEnvironmentVariable("MONITORING_SKIP_COUNT") with
            | null
            | "" -> 3
            | s ->
                match Int32.TryParse s with
                | true, n when n >= 0 -> n
                | _ -> 3

        try
            let outcome =
                MonitoringBatch.runMonitoringJob cs config name jobParams processed skipped

            match outcome.LogPublished with
            | Ok _ -> printfn "{\"level\":\"Information\",\"message\":\"CloudWatch log published\"}"
            | Error e -> eprintfn "{\"level\":\"Warning\",\"message\":\"CloudWatch log skipped\",\"reason\":\"%s\"}" e

            match outcome.MetricsPublished with
            | Ok _ -> printfn "{\"level\":\"Information\",\"message\":\"CloudWatch metrics published\"}"
            | Error e ->
                eprintfn "{\"level\":\"Warning\",\"message\":\"CloudWatch metrics skipped\",\"reason\":\"%s\"}" e

            0
        with ex ->
            eprintfn "Batch failed: %s" ex.Message
            1
    | Some name, _ ->
        eprintfn "Unknown job: %s" name
        2

[<EntryPoint>]
let main args = run args
