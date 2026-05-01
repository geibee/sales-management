module SalesManagement.Infrastructure.CloudWatchPublisher

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text

type BatchMetrics =
    { ProcessedCount: int
      ExecutionTimeMs: int64
      SkipCount: int
      ErrorCount: int }

type PublisherConfig =
    { EndpointUrl: string
      Region: string
      Namespace: string
      LogGroupName: string }

let defaultConfig: PublisherConfig =
    { EndpointUrl = "http://localhost:4566"
      Region = "ap-northeast-1"
      Namespace = "BatchProcessing"
      LogGroupName = "/batch/sales-management" }

let resolveConfig () : PublisherConfig =
    let env name fallback =
        match Environment.GetEnvironmentVariable(name) with
        | null
        | "" -> fallback
        | v -> v

    { EndpointUrl = env "CLOUDWATCH_ENDPOINT_URL" defaultConfig.EndpointUrl
      Region = env "AWS_REGION" defaultConfig.Region
      Namespace = env "CLOUDWATCH_NAMESPACE" defaultConfig.Namespace
      LogGroupName = env "CLOUDWATCH_LOG_GROUP" defaultConfig.LogGroupName }

let private nowIso () = DateTime.UtcNow.ToString("o")

let formatJobCompletedLog (jobName: string) (metrics: BatchMetrics) : string =
    sprintf
        "{\"timestamp\":\"%s\",\"level\":\"Information\",\"message\":\"Job completed\",\"job\":\"%s\",\"metrics\":{\"processedCount\":%d,\"executionTimeMs\":%d,\"skipCount\":%d,\"errorCount\":%d}}"
        (nowIso ())
        jobName
        metrics.ProcessedCount
        metrics.ExecutionTimeMs
        metrics.SkipCount
        metrics.ErrorCount

let formatErrorLog (jobName: string) (errorMessage: string) : string =
    sprintf
        "{\"timestamp\":\"%s\",\"level\":\"Error\",\"message\":\"Job failed\",\"job\":\"%s\",\"error\":\"%s\"}"
        (nowIso ())
        jobName
        (errorMessage.Replace("\"", "'"))

/// Build the JSON body for CloudWatch Logs PutLogEvents.
let buildPutLogEventsBody (logGroupName: string) (logStreamName: string) (message: string) : string =
    let timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    let escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"")

    sprintf
        "{\"logGroupName\":\"%s\",\"logStreamName\":\"%s\",\"logEvents\":[{\"timestamp\":%d,\"message\":\"%s\"}]}"
        logGroupName
        logStreamName
        timestampMs
        escaped

/// Build the form-encoded body for CloudWatch PutMetricData.
let buildPutMetricDataBody (ns: string) (jobName: string) (metrics: BatchMetrics) : string =
    let pairs (i: int) (name: string) (value: int64) =
        [ sprintf "MetricData.member.%d.MetricName=%s" i name
          sprintf "MetricData.member.%d.Value=%d" i value
          sprintf "MetricData.member.%d.Unit=Count" i
          sprintf "MetricData.member.%d.Dimensions.member.1.Name=JobName" i
          sprintf "MetricData.member.%d.Dimensions.member.1.Value=%s" i jobName ]

    let header =
        [ "Action=PutMetricData"; "Version=2010-08-01"; sprintf "Namespace=%s" ns ]

    let entries =
        [ pairs 1 "ProcessedCount" (int64 metrics.ProcessedCount)
          pairs 2 "ExecutionTime" metrics.ExecutionTimeMs
          pairs 3 "SkipCount" (int64 metrics.SkipCount)
          pairs 4 "ErrorCount" (int64 metrics.ErrorCount) ]
        |> List.concat

    String.Join("&", header @ entries)

let private dummyAuthHeader (region: string) (service: string) : string =
    let date = DateTime.UtcNow.ToString("yyyyMMdd")

    sprintf
        "AWS4-HMAC-SHA256 Credential=test/%s/%s/%s/aws4_request, SignedHeaders=host;x-amz-date, Signature=00"
        date
        region
        service

let private postWithTimeout (url: string) (configure: HttpRequestMessage -> unit) : Result<unit, string> =
    try
        use client = new HttpClient()
        client.Timeout <- TimeSpan.FromSeconds(3.0)
        let req = new HttpRequestMessage(HttpMethod.Post, url)
        configure req
        let response = client.SendAsync(req).GetAwaiter().GetResult()

        if response.IsSuccessStatusCode then
            Ok()
        else
            Error(sprintf "HTTP %d" (int response.StatusCode))
    with ex ->
        Error ex.Message

/// Send a single log message to CloudWatch Logs. Returns Error if LocalStack/AWS is unreachable.
let tryPublishLog (config: PublisherConfig) (logStreamName: string) (message: string) : Result<unit, string> =
    let body = buildPutLogEventsBody config.LogGroupName logStreamName message

    postWithTimeout config.EndpointUrl (fun req ->
        req.Content <- new StringContent(body, Encoding.UTF8, "application/x-amz-json-1.1")

        req.Headers.TryAddWithoutValidation("X-Amz-Target", "Logs_20140328.PutLogEvents")
        |> ignore

        req.Headers.TryAddWithoutValidation("X-Amz-Date", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"))
        |> ignore

        req.Headers.Authorization <-
            AuthenticationHeaderValue("AWS4-HMAC-SHA256", dummyAuthHeader config.Region "logs"))

/// Send batch metrics to CloudWatch. Returns Error if LocalStack/AWS is unreachable.
let tryPublishMetrics (config: PublisherConfig) (jobName: string) (metrics: BatchMetrics) : Result<unit, string> =
    let body = buildPutMetricDataBody config.Namespace jobName metrics

    postWithTimeout config.EndpointUrl (fun req ->
        req.Content <- new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")

        req.Headers.TryAddWithoutValidation("X-Amz-Date", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"))
        |> ignore

        req.Headers.Authorization <-
            AuthenticationHeaderValue("AWS4-HMAC-SHA256", dummyAuthHeader config.Region "monitoring"))
