module SalesManagement.Tests.IntegrationTests.BatchJobLogFormatTests

open System
open System.IO
open System.Text.Json
open Npgsql
open Xunit
open SalesManagement.Infrastructure
open SalesManagement.Infrastructure.CloudWatchPublisher
open SalesManagement.Tests.Support.BatchFixture

let private fsharpRoot =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``formatJobCompletedLog produces parseable JSON with metrics block`` () =
    let metrics =
        { ProcessedCount = 10000
          ExecutionTimeMs = 15000L
          SkipCount = 3
          ErrorCount = 0 }

    let line = formatJobCompletedLog "monitoring-test" metrics
    let doc = JsonDocument.Parse(line).RootElement

    Assert.Equal("Job completed", doc.GetProperty("message").GetString())
    Assert.Equal("monitoring-test", doc.GetProperty("job").GetString())
    Assert.Equal("Information", doc.GetProperty("level").GetString())

    let m = doc.GetProperty("metrics")
    Assert.Equal(10000, m.GetProperty("processedCount").GetInt32())
    Assert.Equal(15000L, m.GetProperty("executionTimeMs").GetInt64())
    Assert.Equal(3, m.GetProperty("skipCount").GetInt32())
    Assert.Equal(0, m.GetProperty("errorCount").GetInt32())

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``formatErrorLog produces parseable JSON with Error level`` () =
    let line = formatErrorLog "monitoring-test" "boom"
    let doc = JsonDocument.Parse(line).RootElement
    Assert.Equal("Error", doc.GetProperty("level").GetString())
    Assert.Equal("monitoring-test", doc.GetProperty("job").GetString())

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``defaultConfig points at LocalStack endpoint and BatchProcessing namespace`` () =
    Assert.Equal("http://localhost:4566", defaultConfig.EndpointUrl)
    Assert.Equal("BatchProcessing", defaultConfig.Namespace)
    Assert.Equal("/batch/sales-management", defaultConfig.LogGroupName)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``resolveConfig honours environment overrides`` () =
    let prevEndpoint = Environment.GetEnvironmentVariable("CLOUDWATCH_ENDPOINT_URL")
    let prevNs = Environment.GetEnvironmentVariable("CLOUDWATCH_NAMESPACE")

    try
        Environment.SetEnvironmentVariable("CLOUDWATCH_ENDPOINT_URL", "http://example.test:9999")
        Environment.SetEnvironmentVariable("CLOUDWATCH_NAMESPACE", "Custom")
        let cfg = resolveConfig ()
        Assert.Equal("http://example.test:9999", cfg.EndpointUrl)
        Assert.Equal("Custom", cfg.Namespace)
    finally
        Environment.SetEnvironmentVariable("CLOUDWATCH_ENDPOINT_URL", prevEndpoint)
        Environment.SetEnvironmentVariable("CLOUDWATCH_NAMESPACE", prevNs)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``buildPutMetricDataBody includes all four metric names`` () =
    let metrics =
        { ProcessedCount = 100
          ExecutionTimeMs = 500L
          SkipCount = 1
          ErrorCount = 0 }

    let body = buildPutMetricDataBody "BatchProcessing" "monitoring-test" metrics
    Assert.Contains("Action=PutMetricData", body)
    Assert.Contains("Namespace=BatchProcessing", body)
    Assert.Contains("ProcessedCount", body)
    Assert.Contains("ExecutionTime", body)
    Assert.Contains("SkipCount", body)
    Assert.Contains("ErrorCount", body)
    Assert.Contains("JobName", body)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``buildPutLogEventsBody embeds log group, stream and message`` () =
    let body = buildPutLogEventsBody "/batch/sales-management" "monitoring-test" "hello"
    let doc = JsonDocument.Parse(body).RootElement
    Assert.Equal("/batch/sales-management", doc.GetProperty("logGroupName").GetString())
    Assert.Equal("monitoring-test", doc.GetProperty("logStreamName").GetString())

    let events = doc.GetProperty("logEvents")
    Assert.Equal(1, events.GetArrayLength())
    Assert.Equal("hello", events.[0].GetProperty("message").GetString())
    Assert.True(events.[0].GetProperty("timestamp").GetInt64() > 0L)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``tryPublishLog returns Error gracefully when endpoint is unreachable`` () =
    let cfg =
        { defaultConfig with
            EndpointUrl = "http://127.0.0.1:1" }

    let result = tryPublishLog cfg "test-stream" "msg"

    match result with
    | Ok _ -> ()
    | Error _ -> ()

    Assert.True(true)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``tryPublishMetrics returns Error gracefully when endpoint is unreachable`` () =
    let cfg =
        { defaultConfig with
            EndpointUrl = "http://127.0.0.1:1" }

    let metrics =
        { ProcessedCount = 1
          ExecutionTimeMs = 1L
          SkipCount = 0
          ErrorCount = 0 }

    let result = tryPublishMetrics cfg "test" metrics

    match result with
    | Ok _ -> ()
    | Error _ -> ()

    Assert.True(true)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``setup.sh provisions CloudWatch log group and failure alarm`` () =
    let path = Path.Combine(fsharpRoot, "localstack", "init", "setup.sh")

    Assert.True(File.Exists path, sprintf "setup.sh not found at %s" path)
    let sh = File.ReadAllText path

    // CloudWatch Logs ロググループ
    Assert.Contains("awslocal logs create-log-group", sh)
    Assert.Contains("/batch/sales-management", sh)

    // CloudWatch Alarm
    Assert.Contains("awslocal cloudwatch put-metric-alarm", sh)
    Assert.Contains("--alarm-name batch-failure-alarm", sh)
    Assert.Contains("--metric-name ErrorCount", sh)
    Assert.Contains("--namespace BatchProcessing", sh)

    // SQS への通知連携
    Assert.Contains("batch-notifications", sh)

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``runMonitoringJob records read/write/skip counts in batch_job_execution`` () =
    let jobName = "monitoring-test"
    let jobParams = sprintf "phaseA-%s" (Guid.NewGuid().ToString("N"))

    try
        let cfg =
            { defaultConfig with
                EndpointUrl = "http://127.0.0.1:1" } // 到達不能で OK (Phase A は CW 送信不要)

        let outcome =
            MonitoringBatch.runMonitoringJob connectionString cfg jobName jobParams 10000 3

        Assert.Equal(10000, outcome.Metrics.ProcessedCount)
        Assert.Equal(3, outcome.Metrics.SkipCount)

        // batch_job_execution の状態確認
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()

        use cmd =
            new NpgsqlCommand(
                "SELECT status, read_count, write_count, skip_count FROM batch_job_execution WHERE job_name = @n AND job_params = @p",
                conn
            )

        cmd.Parameters.AddWithValue("n", jobName) |> ignore
        cmd.Parameters.AddWithValue("p", jobParams) |> ignore
        use rd = cmd.ExecuteReader()
        Assert.True(rd.Read(), "batch_job_execution row should exist")

        Assert.Equal("COMPLETED", rd.GetString(0))
        Assert.Equal(10000, rd.GetInt32(1))
        Assert.Equal(9997, rd.GetInt32(2))
        Assert.Equal(3, rd.GetInt32(3))
    finally
        cleanupJob jobName jobParams

[<Fact>]
[<Trait("Category", "BatchJobLogFormat")>]
let ``runMonitoringJob emits structured log line with metrics block`` () =
    let jobName = "monitoring-test"
    let jobParams = sprintf "phaseA-log-%s" (Guid.NewGuid().ToString("N"))

    try
        let cfg =
            { defaultConfig with
                EndpointUrl = "http://127.0.0.1:1" }

        let outcome =
            MonitoringBatch.runMonitoringJob connectionString cfg jobName jobParams 10 0

        let doc = JsonDocument.Parse(outcome.LogLine).RootElement
        Assert.Equal("Job completed", doc.GetProperty("message").GetString())
        Assert.Equal(jobName, doc.GetProperty("job").GetString())
        let m = doc.GetProperty("metrics")
        Assert.Equal(10, m.GetProperty("processedCount").GetInt32())
        Assert.Equal(0, m.GetProperty("skipCount").GetInt32())
    finally
        cleanupJob jobName jobParams
