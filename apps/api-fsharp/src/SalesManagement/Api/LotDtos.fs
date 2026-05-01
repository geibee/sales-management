module SalesManagement.Api.LotDtos

open System
open SalesManagement.Domain.Types
open SalesManagement.Domain.SmartConstructors
open SalesManagement.Domain.Errors

[<CLIMutable>]
type LotNumberDto =
    { year: int
      location: string
      seq: int }

[<CLIMutable>]
type LotDetailDto =
    { itemCategory: string
      premiumCategory: string
      productCategoryCode: string
      lengthSpecLower: decimal
      thicknessSpecLower: decimal
      thicknessSpecUpper: decimal
      qualityGrade: string
      count: int
      quantity: decimal
      inspectionResultCategory: string }

[<CLIMutable>]
type CreateLotRequest =
    { lotNumber: LotNumberDto
      divisionCode: int
      departmentCode: int
      sectionCode: int
      processCategory: int
      inspectionCategory: int
      manufacturingCategory: int
      details: LotDetailDto[] }

[<CLIMutable>]
type DateVersionRequest =
    { date: string; version: Nullable<int> }

[<CLIMutable>]
type DeadlineVersionRequest =
    { deadline: string
      version: Nullable<int> }

[<CLIMutable>]
type InstructItemConversionRequest =
    { destinationItem: string
      version: Nullable<int> }

[<CLIMutable>]
type VersionOnlyRequest = { version: Nullable<int> }

type LotResponse =
    { status: string
      lotNumber: string
      manufacturingCompletedDate: string option
      shippingDeadlineDate: string option
      shippedDate: string option
      destinationItem: string option
      version: int }

type CreateLotResponse =
    { status: string
      lotNumber: string
      version: int }

let private nullToOption (s: string) : string option =
    if String.IsNullOrEmpty s then None else Some s

let private validateLotNumber (dto: LotNumberDto) : Result<LotNumber, ValidationError list> =
    let yearR =
        Validation.liftField
            "lotNumber.year"
            (PositiveInt.tryCreate dto.year
             |> Result.mapError (fun _ -> "Year must be positive"))

    let locationR =
        Validation.liftField
            "lotNumber.location"
            (NonEmptyString.tryCreate dto.location
             |> Result.mapError (fun _ -> "Location must not be empty"))

    let seqR =
        Validation.liftField
            "lotNumber.seq"
            (PositiveInt.tryCreate dto.seq
             |> Result.mapError (fun _ -> "Seq must be positive"))

    Validation.zip yearR (Validation.zip locationR seqR)
    |> Result.map (fun (y, (l, s)) ->
        { Year = PositiveInt.value y
          Location = NonEmptyString.value l
          Seq = PositiveInt.value s })

let private validateDetail (index: int) (dto: LotDetailDto) : Result<LotDetail, ValidationError list> =
    let prefix = sprintf "details[%d]" index

    let categoryR =
        match ItemCategory.tryParse dto.itemCategory with
        | Some c -> Ok c
        | None ->
            Error
                [ { Field = sprintf "%s.itemCategory" prefix
                    Message = sprintf "Unknown itemCategory: %s" dto.itemCategory } ]

    let countR =
        Validation.liftField
            (sprintf "%s.count" prefix)
            (Count.tryCreate dto.count |> Result.mapError (fun _ -> "Count must be positive"))

    let quantityR =
        Validation.liftField
            (sprintf "%s.quantity" prefix)
            (Quantity.tryCreate dto.quantity
             |> Result.mapError (fun _ -> "Quantity must be >= 0.001"))

    Validation.zip categoryR (Validation.zip countR quantityR)
    |> Result.map (fun (category, (count, quantity)) ->
        { ItemCategory = category
          PremiumCategory = nullToOption dto.premiumCategory
          ProductCategoryCode = dto.productCategoryCode
          LengthSpecLower = dto.lengthSpecLower
          ThicknessSpecLower = dto.thicknessSpecLower
          ThicknessSpecUpper = dto.thicknessSpecUpper
          QualityGrade = dto.qualityGrade
          Count = count
          Quantity = quantity
          InspectionResultCategory = nullToOption dto.inspectionResultCategory })

let private validateDetails (dtos: LotDetailDto[]) : Result<NonEmptyList<LotDetail>, ValidationError list> =
    if isNull dtos || dtos.Length = 0 then
        Error
            [ { Field = "details"
                Message = "must contain at least one item" } ]
    else
        let folder
            (acc: Result<LotDetail list, ValidationError list>)
            (i: int)
            (dto: LotDetailDto)
            : Result<LotDetail list, ValidationError list> =
            Validation.zip acc (validateDetail i dto) |> Result.map (fun (xs, d) -> d :: xs)

        let folded =
            dtos |> Array.indexed |> Array.fold (fun acc (i, d) -> folder acc i d) (Ok [])

        match folded with
        | Error e -> Error e
        | Ok rev ->
            match NonEmptyList.ofList (List.rev rev) with
            | None ->
                Error
                    [ { Field = "details"
                        Message = "must contain at least one item" } ]
            | Some nel -> Ok nel

let validateCreateLotRequest (dto: CreateLotRequest) : Result<InventoryLot, ValidationError list> =
    Validation.zip (validateLotNumber dto.lotNumber) (validateDetails dto.details)
    |> Result.map (fun (lotNumber, details) ->
        let common: LotCommon =
            { LotNumber = lotNumber
              DivisionCode = DivisionCode dto.divisionCode
              DepartmentCode = DepartmentCode dto.departmentCode
              SectionCode = SectionCode dto.sectionCode
              ProcessCategory = dto.processCategory
              InspectionCategory = dto.inspectionCategory
              ManufacturingCategory = dto.manufacturingCategory
              Details = details }

        Manufacturing { Common = common })
