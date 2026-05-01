module SalesManagement.Api.ExternalApiRoutes

open System.Text.Json
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Giraffe
open SalesManagement.Domain.Types
open SalesManagement.Infrastructure.ExternalPricingClient

let private writeJson (status: int) (body: obj) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        ctx.Response.StatusCode <- status
        ctx.Response.ContentType <- "application/problem+json"

        let opts =
            JsonSerializerOptions(
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            )

        let payload = JsonSerializer.Serialize(body, opts)
        do! ctx.Response.WriteAsync(payload)
        return Some ctx
    }

[<CLIMutable>]
type ExternalProblem =
    { Type: string
      Title: string
      Status: int
      Detail: string }

let private respondError (err: PriceCheckError) : HttpHandler =
    match err with
    | UpstreamTimeout ms ->
        writeJson
            502
            { Type = "external-service-error"
              Title = "External service error"
              Status = 502
              Detail = sprintf "Request timed out after %dms" ms }
    | UpstreamCircuitOpen ->
        writeJson
            503
            { Type = "external-service-unavailable"
              Title = "External service unavailable"
              Status = 503
              Detail = "Circuit breaker is OPEN for external-pricing-api" }
    | UpstreamStatus code ->
        writeJson
            502
            { Type = "external-service-error"
              Title = "External service error"
              Status = 502
              Detail = sprintf "Upstream returned status %d" code }
    | UpstreamMalformed detail ->
        writeJson
            502
            { Type = "external-service-error"
              Title = "External service error"
              Status = 502
              Detail = detail }

[<CLIMutable>]
type PriceCheckResponse =
    { basePrice: decimal
      adjustmentRate: System.Nullable<decimal>
      source: string }

let priceCheckHandler () : HttpHandler =
    fun next ctx -> task {
        let lotIdRaw =
            match ctx.Request.Query.TryGetValue("lotId") with
            | true, values when values.Count > 0 -> values.[0]
            | _ -> ""

        if System.String.IsNullOrEmpty lotIdRaw then
            return!
                writeJson
                    400
                    { Type = "bad-request"
                      Title = "Bad Request"
                      Status = 400
                      Detail = "lotId is required" }
                    next
                    ctx
        elif Option.isNone (LotNumber.tryParse lotIdRaw) then
            return!
                writeJson
                    400
                    { Type = "bad-request"
                      Title = "Bad Request"
                      Status = 400
                      Detail = sprintf "Invalid lotId format: '%s'" lotIdRaw }
                    next
                    ctx
        else
            let client = ctx.RequestServices.GetRequiredService<IExternalPricingClient>()

            let! result = client.FetchPriceAsync(lotIdRaw)

            match result with
            | Error err -> return! respondError err next ctx
            | Ok q ->
                let body: PriceCheckResponse =
                    { basePrice = q.BasePrice
                      adjustmentRate =
                        match q.AdjustmentRate with
                        | Some v -> System.Nullable v
                        | None -> System.Nullable()
                      source = q.Source |> Option.defaultValue "" }

                return! json body next ctx
    }

let routes () : HttpHandler =
    choose [ GET >=> route "/api/external/price-check" >=> priceCheckHandler () ]
