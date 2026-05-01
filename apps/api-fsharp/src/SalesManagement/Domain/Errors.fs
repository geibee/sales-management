module SalesManagement.Domain.Errors

type ValidationError = { Field: string; Message: string }

type DomainError =
    | NotFound of resource: string * id: string
    | ValidationFailed of ValidationError list
    | InvalidStateTransition of detail: string
    | OptimisticLockConflict of resource: string * id: string
    | InternalError of detail: string

[<RequireQualifiedAccess>]
module Validation =
    /// Combine two validation results, accumulating errors when both sides fail.
    let zip
        (a: Result<'a, ValidationError list>)
        (b: Result<'b, ValidationError list>)
        : Result<'a * 'b, ValidationError list> =
        match a, b with
        | Ok x, Ok y -> Ok(x, y)
        | Error e, Ok _ -> Error e
        | Ok _, Error e -> Error e
        | Error e1, Error e2 -> Error(e1 @ e2)

    let map (f: 'a -> 'b) (r: Result<'a, ValidationError list>) : Result<'b, ValidationError list> = Result.map f r

    /// Lift a single-error string into a field-scoped ValidationError list result.
    let liftField (field: string) (r: Result<'a, string>) : Result<'a, ValidationError list> =
        match r with
        | Ok x -> Ok x
        | Error msg -> Error [ { Field = field; Message = msg } ]
