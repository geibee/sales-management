module SalesManagement.Infrastructure.MonitoringBatch

open System.Diagnostics
open SalesManagement.Infrastructure.CloudWatchPublisher

type MonitoringJobOutcome =
    { JobName: string
      JobParams: string
      Metrics: BatchMetrics
      LogLine: string
      LogPublished: Result<unit, string>
      MetricsPublished: Result<unit, string> }

/// 監視テスト用ジョブ。
/// `processedCount` 件の処理を擬似実行し、batch_job_execution に記録、
/// CloudWatch (LocalStack) にログとメトリクスを送信する。
/// 送信先が到達不能な場合は Error を返すだけでスローしない。
let runMonitoringJob
    (connectionString: string)
    (config: PublisherConfig)
    (jobName: string)
    (jobParams: string)
    (processedCount: int)
    (skipCount: int)
    : MonitoringJobOutcome =
    let sw = Stopwatch.StartNew()

    match JobExecutionRepository.tryStart connectionString jobName jobParams with
    | JobExecutionRepository.Started
    | JobExecutionRepository.AlreadyRunning -> ()
    | JobExecutionRepository.AlreadyCompleted ->
        // 再実行を許容するために行を消してから再開する
        use conn = new Npgsql.NpgsqlConnection(connectionString)
        conn.Open()

        use cmd =
            new Npgsql.NpgsqlCommand("DELETE FROM batch_job_execution WHERE job_name = @n AND job_params = @p", conn)

        cmd.Parameters.AddWithValue("n", jobName) |> ignore
        cmd.Parameters.AddWithValue("p", jobParams) |> ignore
        cmd.ExecuteNonQuery() |> ignore

        match JobExecutionRepository.tryStart connectionString jobName jobParams with
        | JobExecutionRepository.Started -> ()
        | _ -> ()

    let writeCount = processedCount - skipCount
    JobExecutionRepository.complete connectionString jobName jobParams processedCount writeCount skipCount
    sw.Stop()

    let metrics =
        { ProcessedCount = processedCount
          ExecutionTimeMs = sw.ElapsedMilliseconds
          SkipCount = skipCount
          ErrorCount = 0 }

    let logLine = formatJobCompletedLog jobName metrics
    printfn "%s" logLine

    let logResult = tryPublishLog config jobName logLine
    let metricsResult = tryPublishMetrics config jobName metrics

    { JobName = jobName
      JobParams = jobParams
      Metrics = metrics
      LogLine = logLine
      LogPublished = logResult
      MetricsPublished = metricsResult }
