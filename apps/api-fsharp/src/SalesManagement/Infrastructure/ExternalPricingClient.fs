module SalesManagement.Infrastructure.ExternalPricingClient

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Polly.CircuitBreaker

type PriceQuote =
    { BasePrice: decimal
      AdjustmentRate: decimal option
      Source: string option }

type PriceCheckError =
    | UpstreamTimeout of timeoutMs: int
    | UpstreamCircuitOpen
    | UpstreamStatus of statusCode: int
    | UpstreamMalformed of detail: string

type IExternalPricingClient =
    abstract member FetchPriceAsync: lotId: string -> Task<Result<PriceQuote, PriceCheckError>>

let private tryProp (root: JsonElement) (name: string) : JsonElement option =
    let mutable v = Unchecked.defaultof<JsonElement>

    if root.TryGetProperty(name, &v) then Some v else None

let private parseQuote (body: string) : Result<PriceQuote, PriceCheckError> =
    try
        use doc = JsonDocument.Parse body
        let root = doc.RootElement

        match tryProp root "basePrice" with
        | None -> Error(UpstreamMalformed "missing basePrice")
        | Some bp ->
            let adjustment =
                tryProp root "adjustmentRate" |> Option.map (fun v -> v.GetDecimal())

            let source = tryProp root "source" |> Option.map (fun v -> v.GetString())

            Ok
                { BasePrice = bp.GetDecimal()
                  AdjustmentRate = adjustment
                  Source = source }
    with ex ->
        Error(UpstreamMalformed ex.Message)

type ExternalPricingClient(http: HttpClient) =
    interface IExternalPricingClient with
        member _.FetchPriceAsync(lotId: string) : Task<Result<PriceQuote, PriceCheckError>> = task {
            let timeoutMs = int http.Timeout.TotalMilliseconds
            let path = sprintf "/api/pricing/%s" lotId

            try
                use! response = http.GetAsync(path)

                if not response.IsSuccessStatusCode then
                    return Error(UpstreamStatus(int response.StatusCode))
                else
                    let! body = response.Content.ReadAsStringAsync()
                    return parseQuote body
            with
            | :? BrokenCircuitException -> return Error UpstreamCircuitOpen
            | :? TaskCanceledException -> return Error(UpstreamTimeout timeoutMs)
            | :? OperationCanceledException -> return Error(UpstreamTimeout timeoutMs)
            | :? HttpRequestException as ex ->
                let inner = ex.InnerException

                if not (isNull inner) && inner :? TaskCanceledException then
                    return Error(UpstreamTimeout timeoutMs)
                else
                    return Error(UpstreamMalformed ex.Message)
        }
