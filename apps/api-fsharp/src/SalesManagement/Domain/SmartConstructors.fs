module SalesManagement.Domain.SmartConstructors

open System

type PositiveInt = private PositiveInt of int

[<RequireQualifiedAccess>]
module PositiveInt =
    let tryCreate (value: int) : Result<PositiveInt, string> =
        if value > 0 then
            Ok(PositiveInt value)
        else
            Error "must be positive"

    let value (PositiveInt n) : int = n

type NonEmptyString = private NonEmptyString of string

[<RequireQualifiedAccess>]
module NonEmptyString =
    let tryCreate (value: string) : Result<NonEmptyString, string> =
        if String.IsNullOrWhiteSpace value then
            Error "must not be empty"
        else
            Ok(NonEmptyString value)

    let value (NonEmptyString s) : string = s
