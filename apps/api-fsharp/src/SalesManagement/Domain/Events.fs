module SalesManagement.Domain.Events

open System

type DomainEvent = LotManufacturingCompleted of lotId: string * date: DateOnly

[<RequireQualifiedAccess>]
module DomainEvent =
    let eventType (e: DomainEvent) : string =
        match e with
        | LotManufacturingCompleted _ -> "LotManufacturingCompleted"

    let payloadJson (e: DomainEvent) : string =
        match e with
        | LotManufacturingCompleted(lotId, date) ->
            sprintf """{"lotId":"%s","date":"%s"}""" lotId (date.ToString("yyyy-MM-dd"))

type EventHandler = DomainEvent -> unit

type EventBus() =
    let handlers = ResizeArray<EventHandler>()

    member _.Subscribe(handler: EventHandler) : unit = handlers.Add handler

    member _.Publish(event: DomainEvent) : unit =
        for h in handlers do
            h event
