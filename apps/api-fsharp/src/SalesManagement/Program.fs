namespace SalesManagement

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.RateLimiting
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.RateLimiting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Diagnostics.HealthChecks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Giraffe
open OpenTelemetry
open OpenTelemetry.Resources
open OpenTelemetry.Trace
open Polly
open Polly.Extensions.Http
open Serilog
open Serilog.Context
open Serilog.Core
open Serilog.Events
open Serilog.Formatting
open SalesManagement.Api
open SalesManagement.Domain.Events
open SalesManagement.Infrastructure
open SalesManagement.Infrastructure.ExternalPricingClient

type ActivityEnricher() =
    interface ILogEventEnricher with
        member _.Enrich(logEvent: LogEvent, factory: ILogEventPropertyFactory) =
            let activity = Activity.Current

            if not (isNull activity) then
                let traceId = activity.TraceId.ToString()
                let spanId = activity.SpanId.ToString()
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", traceId))
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("SpanId", spanId))

type StructuredJsonFormatter() =
    let toCamel (s: string) =
        if String.IsNullOrEmpty s then
            s
        else
            string (Char.ToLowerInvariant s.[0]) + s.Substring(1)

    let renameProperty (key: string) =
        match key with
        | "RequestId" -> "requestId"
        | "TraceId" -> "traceId"
        | "SpanId" -> "spanId"
        | _ -> toCamel key

    let unwrap (v: LogEventPropertyValue) : obj =
        match v with
        | :? ScalarValue as sv ->
            match sv.Value with
            | null -> null
            | x -> x
        | other -> box (other.ToString())

    interface ITextFormatter with
        member _.Format(logEvent: LogEvent, output: TextWriter) =
            let dict = System.Collections.Generic.Dictionary<string, obj>()
            dict.["timestamp"] <- logEvent.Timestamp.UtcDateTime.ToString("o")
            dict.["level"] <- logEvent.Level.ToString()
            dict.["message"] <- logEvent.RenderMessage()

            for kv in logEvent.Properties do
                let key = renameProperty kv.Key

                if not (dict.ContainsKey key) then
                    dict.[key] <- unwrap kv.Value

            if not (isNull logEvent.Exception) then
                dict.["exception"] <- logEvent.Exception.ToString()

            let json = System.Text.Json.JsonSerializer.Serialize(dict :> obj)
            output.WriteLine(json)

module Hosting =

    let private exceptionHandler: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) -> task {
            try
                return! next ctx
            with ex ->
                Log.Error(ex, "Unhandled exception in HTTP pipeline")

                if not ctx.Response.HasStarted then
                    ctx.Response.StatusCode <- 500
                    ctx.Response.ContentType <- "application/problem+json"

                    let body =
                        """{"type":"internal-error","title":"Internal server error","status":500,"detail":"An unexpected error occurred."}"""

                    do! ctx.Response.WriteAsync body

                return Some ctx
        }

    let private securityHeaders: HttpHandler =
        setHttpHeader "X-Content-Type-Options" "nosniff"
        >=> setHttpHeader "X-Frame-Options" "DENY"
        >=> setHttpHeader "X-XSS-Protection" "1; mode=block"
        >=> setHttpHeader "Content-Security-Policy" "default-src 'none'; frame-ancestors 'none'"
        >=> setHttpHeader "Referrer-Policy" "no-referrer"
        >=> setHttpHeader "Cross-Origin-Resource-Policy" "same-origin"

    let private healthHandler (connectionString: string) : HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) -> task {
            let check =
                SalesManagement.Api.HealthChecks.PostgresHealthCheck(connectionString) :> IHealthCheck

            let healthCtx = HealthCheckContext()
            let! result = check.CheckHealthAsync(healthCtx, ctx.RequestAborted)
            let healthy = result.Status = HealthStatus.Healthy
            let label = if healthy then "UP" else "DOWN"
            ctx.Response.StatusCode <- if healthy then 200 else 503
            ctx.Response.ContentType <- "application/json; charset=utf-8"

            let body =
                sprintf """{"status":"%s","checks":{"postgresql":"%s","self":"UP"}}""" label label

            do! ctx.Response.WriteAsync body
            return Some ctx
        }

    let private openapiHandler (openapiPath: string) : HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) -> task {
            if File.Exists openapiPath then
                ctx.Response.ContentType <- "application/yaml; charset=utf-8"
                let! bytes = File.ReadAllBytesAsync openapiPath
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            else
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync "openapi.yaml not found"

            return Some ctx
        }

    let private swaggerHtml =
        """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Sales Management API</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css">
</head>
<body>
<div id="swagger-ui"></div>
<script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
<script>
window.onload = () => {
    window.ui = SwaggerUIBundle({
        url: '/openapi.yaml',
        dom_id: '#swagger-ui',
        deepLinking: true,
        presets: [SwaggerUIBundle.presets.apis]
    });
};
</script>
</body>
</html>"""

    let private swaggerHandler: HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) -> task {
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            do! ctx.Response.WriteAsync swaggerHtml
            return Some ctx
        }

    let private slowHandler: HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) -> task {
            let parseInt (s: string) =
                match Int32.TryParse s with
                | true, v -> Some v
                | false, _ -> None

            let msResult =
                match ctx.Request.Query.TryGetValue "ms" with
                | true, v ->
                    match parseInt (string v) with
                    | Some n when n >= 0 && n <= 5000 -> Ok n
                    | _ -> Error()
                | false, _ -> Ok 1000

            match msResult with
            | Error _ ->
                ctx.Response.StatusCode <- 400
                ctx.Response.ContentType <- "application/problem+json"

                do!
                    ctx.Response.WriteAsync(
                        """{"type":"https://errors.example.com/validation-error","title":"Validation failed","status":400,"detail":"ms must be an integer in [0, 5000]"}"""
                    )
            | Ok ms ->
                try
                    do! Task.Delay(ms, ctx.RequestAborted)
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                    do! ctx.Response.WriteAsync(sprintf """{"slept_ms":%d}""" ms)
                with :? OperationCanceledException ->
                    ()

            return Some ctx
        }

    let private buildHandlers
        (isDev: bool)
        (authOptions: SalesManagement.Api.Auth.AuthOptions)
        (connectionString: string)
        (openapiPath: string)
        : HttpHandler =
        let authEnabled = authOptions.Enabled
        let viewerGate = SalesManagement.Api.Auth.requireViewer authEnabled
        let operatorGate = SalesManagement.Api.Auth.requireOperator authEnabled

        let businessRoutes: HttpHandler =
            choose
                [ LotRoutes.routes connectionString
                  LotListRoutes.routes connectionString
                  SalesCaseRoutes.routes connectionString
                  ReservationCaseRoutes.routes connectionString
                  ConsignmentCaseRoutes.routes connectionString ]

        let externalRoutes: HttpHandler = ExternalApiRoutes.routes ()

        let baseRoutes =
            [ GET >=> route "/health" >=> healthHandler connectionString
              GET >=> route "/openapi.yaml" >=> openapiHandler openapiPath
              GET >=> route "/swagger" >=> swaggerHandler
              GET >=> route "/swagger/" >=> swaggerHandler
              AuthRoutes.routes authOptions
              securityHeaders >=> GET >=> viewerGate >=> externalRoutes
              securityHeaders >=> GET >=> viewerGate >=> businessRoutes
              securityHeaders >=> operatorGate >=> businessRoutes ]

        let allRoutes =
            if isDev then
                (GET >=> route "/test/slow" >=> slowHandler) :: baseRoutes
            else
                baseRoutes

        exceptionHandler >=> choose allRoutes

    let private resolveConnectionString (config: IConfiguration) : string =
        match Environment.GetEnvironmentVariable("DATABASE_URL") with
        | null
        | "" ->
            match config.["Database:ConnectionString"] with
            | null
            | "" -> invalidOp "Database:ConnectionString must be configured (appsettings.json or DATABASE_URL env var)"
            | cs -> cs
        | url -> url

    let private resolvePort (config: IConfiguration) : int option =
        let envPort = Environment.GetEnvironmentVariable("PORT")

        if not (String.IsNullOrEmpty envPort) then
            Some(Int32.Parse envPort)
        else
            match config.["Server:Port"] with
            | null
            | "" -> None
            | p -> Some(Int32.Parse p)

    let private getInt (config: IConfiguration) (key: string) (defaultValue: int) : int =
        match config.[key] with
        | null
        | "" -> defaultValue
        | v -> Int32.Parse v

    let private isTransient (r: HttpResponseMessage) : bool =
        int r.StatusCode >= 500 || int r.StatusCode = 408

    let private buildRetryPolicy (retryCount: int) : IAsyncPolicy<HttpResponseMessage> =
        Policy
            .HandleResult<HttpResponseMessage>(isTransient)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount,
                Func<int, TimeSpan>(fun retry -> TimeSpan.FromMilliseconds(100.0 * float (1 <<< (retry - 1)))),
                Action<Polly.DelegateResult<HttpResponseMessage>, TimeSpan, int, Context>(fun result _ retry _ ->
                    let status =
                        if isNull (box result.Result) then
                            0
                        else
                            int result.Result.StatusCode

                    Log.Warning(
                        "Retry attempt {Attempt} for {Service} status={StatusCode}",
                        retry,
                        "external-pricing-api",
                        status
                    ))
            )
        :> IAsyncPolicy<HttpResponseMessage>

    let private buildCircuitBreaker (failureThreshold: int) : IAsyncPolicy<HttpResponseMessage> =
        Policy
            .HandleResult<HttpResponseMessage>(isTransient)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                failureThreshold,
                TimeSpan.FromSeconds(30.0),
                Action<Polly.DelegateResult<HttpResponseMessage>, TimeSpan>(fun _ _ ->
                    Log.Warning("Circuit breaker OPEN for {Service}", "external-pricing-api")),
                Action(fun () -> Log.Information("Circuit breaker RESET for {Service}", "external-pricing-api"))
            )
        :> IAsyncPolicy<HttpResponseMessage>

    let private registerExternalPricingClient (services: IServiceCollection) (configuration: IConfiguration) : unit =
        let externalPricingUrl =
            match configuration.["ExternalApi:PricingUrl"] with
            | null
            | "" -> "http://localhost:8181"
            | v -> v

        let timeoutMs = getInt configuration "ExternalApi:TimeoutMs" 3000
        let retryCount = getInt configuration "ExternalApi:RetryCount" 3
        let circuitFailures = getInt configuration "ExternalApi:CircuitFailures" 5

        let httpClientBuilder =
            services.AddHttpClient<IExternalPricingClient, ExternalPricingClient>(fun (client: HttpClient) ->
                client.BaseAddress <- Uri externalPricingUrl
                client.Timeout <- TimeSpan.FromMilliseconds(float timeoutMs))

        httpClientBuilder.AddPolicyHandler(buildRetryPolicy retryCount) |> ignore

        httpClientBuilder.AddPolicyHandler(buildCircuitBreaker circuitFailures)
        |> ignore

    let private logManufacturingCompleted (event: DomainEvent) : unit =
        match event with
        | LotManufacturingCompleted(lotId, date) ->
            Log.Information("[Event] Lot {LotId} manufacturing completed on {Date}", lotId, date.ToString("yyyy-MM-dd"))

    let private registerTracing (services: IServiceCollection) (configuration: IConfiguration) : unit =
        let serviceName =
            match configuration.["Telemetry:ServiceName"] with
            | null
            | "" -> "sales-management"
            | v -> v

        let otlpEndpoint =
            match Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") with
            | null
            | "" ->
                match configuration.["Telemetry:OtlpEndpoint"] with
                | null
                | "" -> None
                | v -> Some v
            | v -> Some v

        services
            .AddOpenTelemetry()
            .WithTracing(fun (b: TracerProviderBuilder) ->
                b
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql")
                |> ignore

                match otlpEndpoint with
                | Some endpoint -> b.AddOtlpExporter(fun opts -> opts.Endpoint <- Uri endpoint) |> ignore
                | None -> ())
        |> ignore

    let private registerOutbox
        (services: IServiceCollection)
        (configuration: IConfiguration)
        (connectionString: string)
        : unit =
        let bus = EventBus()
        bus.Subscribe logManufacturingCompleted
        services.AddSingleton<EventBus>(bus) |> ignore
        let pollIntervalMs = getInt configuration "Outbox:PollIntervalMs" 5000

        services.AddSingleton<IHostedService>(fun _ ->
            OutboxProcessor.OutboxProcessor(connectionString, bus, pollIntervalMs) :> IHostedService)
        |> ignore

    let private buildFixedWindowOptions (permitLimit: int) (windowSeconds: int) : FixedWindowRateLimiterOptions =
        FixedWindowRateLimiterOptions(
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(float windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        )

    let private partitionForRequest
        (permitLimit: int)
        (windowSeconds: int)
        (ctx: HttpContext)
        : RateLimitPartition<string> =
        if ctx.Request.Path.StartsWithSegments(PathString "/health") then
            RateLimitPartition.GetNoLimiter("health")
        else
            let factory =
                Func<string, FixedWindowRateLimiterOptions>(fun _ -> buildFixedWindowOptions permitLimit windowSeconds)

            RateLimitPartition.GetFixedWindowLimiter("global", factory)

    let private onRateLimitRejected (windowSeconds: int) (rctx: OnRejectedContext) (_: CancellationToken) : ValueTask =
        rctx.HttpContext.Response.Headers.["Retry-After"] <- StringValues(string windowSeconds)
        ValueTask()

    let private registerRateLimiter (services: IServiceCollection) (configuration: IConfiguration) : unit =
        let permitLimit = getInt configuration "RateLimit:PermitLimit" 1000
        let windowSeconds = getInt configuration "RateLimit:WindowSeconds" 60

        let configure (options: RateLimiterOptions) =
            options.RejectionStatusCode <- 429

            options.OnRejected <-
                Func<OnRejectedContext, CancellationToken, ValueTask>(onRateLimitRejected windowSeconds)

            options.GlobalLimiter <-
                PartitionedRateLimiter.Create<HttpContext, string>(partitionForRequest permitLimit windowSeconds)

        services.AddRateLimiter(configure) |> ignore

    let private registerCors (services: IServiceCollection) (configuration: IConfiguration) : unit =
        let allowedOrigins =
            configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            |> Option.ofObj
            |> Option.defaultValue [||]

        services.AddCors(fun (opts: Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions) ->
            opts.AddDefaultPolicy(fun (policy: Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder) ->
                if allowedOrigins.Length > 0 then
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .SetPreflightMaxAge(TimeSpan.FromMinutes 10.0)
                    |> ignore))
        |> ignore

    let private registerShutdown (services: IServiceCollection) (configuration: IConfiguration) : unit =
        let shutdownSeconds = getInt configuration "Hosting:ShutdownTimeoutSeconds" 30

        services.Configure<HostOptions>(fun (opts: HostOptions) ->
            opts.ShutdownTimeout <- TimeSpan.FromSeconds(float shutdownSeconds))
        |> ignore

    let private useSecurityHeaders (app: WebApplication) : unit =
        app.Use(
            Func<HttpContext, RequestDelegate, Task>(fun (ctx: HttpContext) (next: RequestDelegate) -> task {
                ctx.Response.OnStarting(fun () ->
                    let h = ctx.Response.Headers

                    if not (h.ContainsKey "X-Content-Type-Options") then
                        h.["X-Content-Type-Options"] <- StringValues "nosniff"

                    if not (h.ContainsKey "Cross-Origin-Resource-Policy") then
                        h.["Cross-Origin-Resource-Policy"] <- StringValues "same-origin"

                    Task.CompletedTask)

                do! next.Invoke ctx
            })
        )
        |> ignore

    let createApp (args: string[]) : WebApplication =
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
        let builder = WebApplication.CreateBuilder(args)

        let configuration = builder.Configuration
        let connectionString = resolveConnectionString configuration
        let authOptions = SalesManagement.Api.Auth.read configuration

        let logger =
            LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.With(ActivityEnricher())
                .Enrich.WithProperty("Application", "SalesManagement")
                .WriteTo.Console(formatter = StructuredJsonFormatter())
                .CreateLogger()

        Log.Logger <- logger
        builder.Host.UseSerilog() |> ignore

        match resolvePort configuration with
        | Some p -> builder.WebHost.UseUrls(sprintf "http://0.0.0.0:%d" p) |> ignore
        | None -> ()

        SalesManagement.Api.Auth.configure builder.Services authOptions
        builder.Services.AddGiraffe() |> ignore
        builder.Services.AddMemoryCache() |> ignore

        registerTracing builder.Services configuration
        registerExternalPricingClient builder.Services configuration
        registerOutbox builder.Services configuration connectionString
        registerRateLimiter builder.Services configuration
        registerCors builder.Services configuration
        registerShutdown builder.Services configuration

        let app = builder.Build()

        app.UseCors() |> ignore
        app.UseRateLimiter() |> ignore

        if authOptions.Enabled then
            app.UseAuthentication() |> ignore
            app.UseAuthorization() |> ignore

        useSecurityHeaders app

        app.Use(
            Func<HttpContext, RequestDelegate, Task>(fun (ctx: HttpContext) (next: RequestDelegate) -> task {
                use _ = LogContext.PushProperty("RequestId", ctx.TraceIdentifier)
                do! next.Invoke ctx
            })
        )
        |> ignore

        app.UseSerilogRequestLogging(fun (options: Serilog.AspNetCore.RequestLoggingOptions) ->
            options.EnrichDiagnosticContext <-
                Action<Serilog.IDiagnosticContext, HttpContext>(fun diag httpCtx ->
                    diag.Set("RequestId", httpCtx.TraceIdentifier)))
        |> ignore

        let openapiPath = Path.Combine(AppContext.BaseDirectory, "openapi.yaml")
        app.UseGiraffe(buildHandlers (app.Environment.IsDevelopment()) authOptions connectionString openapiPath)
        app

    [<EntryPoint>]
    let main args =
        let app = createApp args

        try
            try
                app.Run()
                0
            with ex ->
                Log.Fatal(ex, "Host terminated unexpectedly")
                1
        finally
            Log.CloseAndFlush()
