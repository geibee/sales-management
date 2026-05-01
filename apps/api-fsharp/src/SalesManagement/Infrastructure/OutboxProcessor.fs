module SalesManagement.Infrastructure.OutboxProcessor

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Npgsql
open Serilog
open SalesManagement.Domain.Events
open SalesManagement.Infrastructure

let private parseEvent (evt: OutboxRepository.PendingEvent) : DomainEvent option =
    match evt.EventType with
    | "LotManufacturingCompleted" ->
        try
            use doc = System.Text.Json.JsonDocument.Parse evt.Payload
            let root = doc.RootElement
            let lotId = root.GetProperty("lotId").GetString()
            let dateStr = root.GetProperty("date").GetString()
            Some(LotManufacturingCompleted(lotId, DateOnly.Parse dateStr))
        with _ ->
            None
    | _ -> None

type OutboxProcessor(connectionString: string, bus: EventBus, pollIntervalMs: int) =
    let cts = new CancellationTokenSource()
    let mutable workerTask: Task = Task.CompletedTask

    let dispatchEvent (evt: OutboxRepository.PendingEvent) =
        match parseEvent evt with
        | Some domainEvent -> bus.Publish domainEvent
        | None -> Log.Warning("Unknown outbox event type {EventType}", evt.EventType)

    let processOne (conn: NpgsqlConnection) (evt: OutboxRepository.PendingEvent) =
        Log.Information("Processing outbox event {EventType} {EventId}", evt.EventType, evt.Id)

        try
            dispatchEvent evt
            OutboxRepository.markProcessed conn evt.Id
            Log.Information("Outbox event processed {EventId}", evt.Id)
        with ex ->
            Log.Error(ex, "Outbox event handler failed {EventId}", evt.Id)
            OutboxRepository.markFailed conn evt.Id ex.Message

    let processBatch () =
        try
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()
            // claimPending atomically marks rows as 'processing' under
            // FOR UPDATE SKIP LOCKED, so multiple workers do not double-publish.
            let pending = OutboxRepository.claimPending conn 10

            for evt in pending do
                processOne conn evt
        with ex ->
            Log.Error(ex, "Outbox poll failed")

    let runAsync (token: CancellationToken) : Task =
        Task.Run(fun () ->
            task {
                while not token.IsCancellationRequested do
                    processBatch ()

                    try
                        do! Task.Delay(pollIntervalMs, token)
                    with :? OperationCanceledException ->
                        ()
            }
            :> Task)

    interface IHostedService with
        member _.StartAsync(_: CancellationToken) : Task =
            workerTask <- runAsync cts.Token
            Task.CompletedTask

        member _.StopAsync(_: CancellationToken) : Task =
            cts.Cancel()
            workerTask
