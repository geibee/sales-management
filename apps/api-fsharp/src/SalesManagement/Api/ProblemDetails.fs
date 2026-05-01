module SalesManagement.Api.ProblemDetails

open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Http
open Giraffe
open SalesManagement.Domain.Errors

let private serializerOptions =
    let opts =
        JsonSerializerOptions(
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        )

    opts

[<CLIMutable>]
type ProblemBody =
    { Type: string
      Title: string
      Status: int
      Detail: string
      Errors: ValidationError[] }

let private writeProblem (status: int) (body: ProblemBody) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        ctx.Response.StatusCode <- status
        ctx.Response.ContentType <- "application/problem+json"
        let json = JsonSerializer.Serialize(body, serializerOptions)
        do! ctx.Response.WriteAsync(json)
        return Some ctx
    }

let badRequest (detail: string) : HttpHandler =
    writeProblem
        400
        { Type = "bad-request"
          Title = "Bad Request"
          Status = 400
          Detail = detail
          Errors = null }

let notFound (detail: string) : HttpHandler =
    writeProblem
        404
        { Type = "not-found"
          Title = "Not Found"
          Status = 404
          Detail = detail
          Errors = null }

let conflict (detail: string) : HttpHandler =
    writeProblem
        409
        { Type = "conflict"
          Title = "Conflict"
          Status = 409
          Detail = detail
          Errors = null }

let toResponse (resourceName: string) (err: DomainError) : HttpHandler =
    match err with
    | NotFound(_, id) ->
        writeProblem
            404
            { Type = "not-found"
              Title = "Resource not found"
              Status = 404
              Detail = sprintf "%s %s not found" resourceName id
              Errors = null }
    | InvalidStateTransition detail ->
        writeProblem
            400
            { Type = "invalid-state-transition"
              Title = "Invalid state transition"
              Status = 400
              Detail = detail
              Errors = null }
    | OptimisticLockConflict(_, id) ->
        writeProblem
            409
            { Type = "optimistic-lock-conflict"
              Title = "Resource was modified by another user"
              Status = 409
              Detail = sprintf "%s %s has been updated. Please reload and try again." resourceName id
              Errors = null }
    | ValidationFailed errors ->
        writeProblem
            400
            { Type = "validation-error"
              Title = "Validation failed"
              Status = 400
              Detail = null
              Errors = errors |> List.toArray }
    | InternalError detail ->
        writeProblem
            500
            { Type = "internal-error"
              Title = "Internal server error"
              Status = 500
              Detail = detail
              Errors = null }
