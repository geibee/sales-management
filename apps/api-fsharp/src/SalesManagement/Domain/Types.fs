module SalesManagement.Domain.Types

type NonEmptyList<'a> = { Head: 'a; Tail: 'a list }

[<RequireQualifiedAccess>]
module NonEmptyList =
    let ofList (xs: 'a list) : NonEmptyList<'a> option =
        match xs with
        | [] -> None
        | head :: tail -> Some { Head = head; Tail = tail }

    let toList (nel: NonEmptyList<'a>) : 'a list = nel.Head :: nel.Tail

type DivisionCode = DivisionCode of int
type DepartmentCode = DepartmentCode of int
type SectionCode = SectionCode of int

type LotNumber =
    { Year: int
      Location: string
      Seq: int }

[<RequireQualifiedAccess>]
module LotNumber =
    let toString (n: LotNumber) : string =
        sprintf "%d-%s-%03d" n.Year n.Location n.Seq

    let tryParse (s: string) : LotNumber option =
        let parts = s.Split('-')

        if parts.Length <> 3 then
            None
        else
            match System.Int32.TryParse parts.[0], System.Int32.TryParse parts.[2] with
            | (true, year), (true, seq) when parts.[1] <> "" ->
                Some
                    { Year = year
                      Location = parts.[1]
                      Seq = seq }
            | _ -> None

type Amount = private Amount of int

[<RequireQualifiedAccess>]
module Amount =
    let tryCreate (value: int) : Result<Amount, string> =
        if value >= 0 then
            Ok(Amount value)
        else
            Error "Amount must be >= 0"

    let value (Amount v) : int = v

type Quantity = private Quantity of decimal

[<RequireQualifiedAccess>]
module Quantity =
    let tryCreate (value: decimal) : Result<Quantity, string> =
        if value >= 0.001m then
            Ok(Quantity value)
        else
            Error "Quantity must be >= 0.001"

    let value (Quantity v) : decimal = v

type Count = private Count of int

[<RequireQualifiedAccess>]
module Count =
    let tryCreate (value: int) : Result<Count, string> =
        if value >= 1 then
            Ok(Count value)
        else
            Error "Count must be >= 1"

    let value (Count v) : int = v

type ItemCategory =
    | General
    | Premium
    | Custom

[<RequireQualifiedAccess>]
module ItemCategory =
    let toString =
        function
        | General -> "general"
        | Premium -> "premium"
        | Custom -> "custom"

    let tryParse =
        function
        | "general" -> Some General
        | "premium" -> Some Premium
        | "custom" -> Some Custom
        | _ -> None

type LotDetail =
    { ItemCategory: ItemCategory
      PremiumCategory: string option
      ProductCategoryCode: string
      LengthSpecLower: decimal
      ThicknessSpecLower: decimal
      ThicknessSpecUpper: decimal
      QualityGrade: string
      Count: Count
      Quantity: Quantity
      InspectionResultCategory: string option }

type LotCommon =
    { LotNumber: LotNumber
      DivisionCode: DivisionCode
      DepartmentCode: DepartmentCode
      SectionCode: SectionCode
      ProcessCategory: int
      InspectionCategory: int
      ManufacturingCategory: int
      Details: LotDetail NonEmptyList }

type ManufacturingLot = { Common: LotCommon }

type ManufacturedLot =
    { Common: LotCommon
      ManufacturingCompletedDate: System.DateOnly }

type ConversionDestinationInfo = { DestinationItem: string }

type ConversionInstructedLot =
    { Common: LotCommon
      ManufacturingCompletedDate: System.DateOnly
      DestinationInfo: ConversionDestinationInfo }

type ShippingInstructedLot =
    { Common: LotCommon
      ManufacturingCompletedDate: System.DateOnly
      ShippingDeadlineDate: System.DateOnly }

type ShippedLot =
    { Common: LotCommon
      ManufacturingCompletedDate: System.DateOnly
      ShippingDeadlineDate: System.DateOnly
      ShippedDate: System.DateOnly }

type InventoryLot =
    | Manufacturing of ManufacturingLot
    | Manufactured of ManufacturedLot
    | ConversionInstructed of ConversionInstructedLot
    | ShippingInstructed of ShippingInstructedLot
    | Shipped of ShippedLot

[<RequireQualifiedAccess>]
module InventoryLot =
    let common =
        function
        | Manufacturing l -> l.Common
        | Manufactured l -> l.Common
        | ConversionInstructed l -> l.Common
        | ShippingInstructed l -> l.Common
        | Shipped l -> l.Common

    let statusString =
        function
        | Manufacturing _ -> "manufacturing"
        | Manufactured _ -> "manufactured"
        | ConversionInstructed _ -> "conversion_instructed"
        | ShippingInstructed _ -> "shipping_instructed"
        | Shipped _ -> "shipped"
