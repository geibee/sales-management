module SalesManagement.Api.LotDtos

open System
open System.Text.Json
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

type LotDetailResponse =
    { itemCategory: string
      premiumCategory: string option
      productCategoryCode: string
      lengthSpecLower: decimal
      thicknessSpecLower: decimal
      thicknessSpecUpper: decimal
      qualityGrade: string
      count: int
      quantity: decimal
      inspectionResultCategory: string option }

type CodeNameResponse = { code: int; name: string option }

type LotResponse =
    { status: string
      lotNumber: string
      manufacturingCompletedDate: string option
      shippingDeadlineDate: string option
      shippedDate: string option
      destinationItem: string option
      division: CodeNameResponse
      department: CodeNameResponse
      section: CodeNameResponse
      processCategory: CodeNameResponse
      inspectionCategory: CodeNameResponse
      manufacturingCategory: CodeNameResponse
      details: LotDetailResponse list
      version: int }

type CreateLotResponse =
    { status: string
      lotNumber: string
      version: int }

let private nullToOption (s: string) : string option =
    if String.IsNullOrEmpty s then None else Some s

/// '-' や空白を含む location は LotNumber.toString/tryParse の round-trip を壊し、
/// 作成後に GET できないロットになる (Schemathesis 検出)。
let private tryCreateLocation (raw: string) : Result<NonEmptyString, string> =
    NonEmptyString.tryCreate raw
    |> Result.mapError (fun _ -> "Location must not be empty")
    |> Result.bind (fun loc ->
        if NonEmptyString.value loc |> Seq.exists (fun c -> c = '-' || Char.IsWhiteSpace c) then
            Error "Location must not contain '-' or whitespace"
        else
            Ok loc)

let private validateLotNumber (dto: LotNumberDto) : Result<LotNumber, ValidationError list> =
    if isNull (box dto) then
        // body が {} のとき CLIMutable レコードは null で bind される。
        // null のままフィールドへ触ると NRE → 500 になる (Schemathesis 検出)
        Error
            [ { Field = "lotNumber"
                Message = "lotNumber is required" } ]
    else

        let yearR =
            Validation.liftField
                "lotNumber.year"
                (PositiveInt.tryCreate dto.year
                 |> Result.mapError (fun _ -> "Year must be positive"))

        let locationR =
            Validation.liftField "lotNumber.location" (tryCreateLocation dto.location)

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

/// NUL (U+0000) は Postgres の TEXT 列に格納できず DbException → 500 になる。
/// 契約上も意味を持たないため検証段階で拒否する (Schemathesis 検出)。
let private containsNul (s: string) : bool = not (isNull s) && s.Contains '\u0000'

/// 仕様値 (openapi.yaml) と対のレンジ。無制限だと JSON→Decimal 変換の限界が
/// 実質の境界になり、契約から観測できない挙動になる。
let private validateSpecRanges (prefix: string) (dto: LotDetailDto) : Result<unit, ValidationError list> =
    [ "lengthSpecLower", dto.lengthSpecLower
      "thicknessSpecLower", dto.thicknessSpecLower
      "thicknessSpecUpper", dto.thicknessSpecUpper ]
    |> List.choose (fun (name, v) ->
        if v < -9999999999m || v > 9999999999m then
            Some
                { Field = sprintf "%s.%s" prefix name
                  Message = "must be between -9999999999 and 9999999999" }
        else
            None)
    |> function
        | [] -> Ok()
        | errors -> Error errors

let private validateRequiredDetailStrings (prefix: string) (dto: LotDetailDto) : Result<unit, ValidationError list> =
    [ "productCategoryCode", dto.productCategoryCode
      "qualityGrade", dto.qualityGrade ]
    |> List.choose (fun (name, value) ->
        if isNull value then
            Some
                { Field = sprintf "%s.%s" prefix name
                  Message = "is required" }
        else
            None)
    |> function
        | [] -> Ok()
        | errors -> Error errors

let private validateDetail (index: int) (dto: LotDetailDto) : Result<LotDetail, ValidationError list> =
    if isNull (box dto) then
        // details: [null] のとき要素が null で bind される (Schemathesis 検出)
        Error
            [ { Field = sprintf "details[%d]" index
                Message = "detail must not be null" } ]
    else

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
                 |> Result.mapError (fun _ -> "Quantity must be >= 0.001")
                 |> Result.bind (fun q ->
                     if Quantity.value q > 9999999999m then
                         Error "Quantity must be <= 9999999999"
                     else
                         Ok q))

        let requiredStringsR = validateRequiredDetailStrings prefix dto

        // 仕様値 (openapi.yaml) と対のレンジ。無制限だと JSON→Decimal 変換の限界が
        // 実質の境界になり、契約から観測できない挙動になる
        let specRangeR = validateSpecRanges prefix dto

        Validation.zip
            requiredStringsR
            (Validation.zip specRangeR (Validation.zip categoryR (Validation.zip countR quantityR)))
        |> Result.map (fun ((), ((), rest)) -> rest)
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

let private tryGetJsonProperty (name: string) (element: JsonElement) : JsonElement option =
    if element.ValueKind <> JsonValueKind.Object then
        None
    else
        let mutable value = Unchecked.defaultof<JsonElement>

        if element.TryGetProperty(name, &value) then
            Some value
        else
            None

let private missingProperties (prefix: string) (required: string list) (element: JsonElement) : ValidationError list =
    if element.ValueKind <> JsonValueKind.Object then
        []
    else
        required
        |> List.choose (fun name ->
            match tryGetJsonProperty name element with
            | Some _ -> None
            | None ->
                Some
                    { Field = prefix + name
                      Message = "is required" })

/// System.Text.Json は欠落した数値を 0 に束縛するため、0 が正当値のプロパティでは
/// 「欠落」と「明示的な 0」を DTO だけで区別できない。OpenAPI の required と対になる
/// 存在検査を JSON 境界で先に行い、永続化や重複判定より前に 400 を確定させる。
let validateCreateLotRequiredProperties (root: JsonElement) : ValidationError list =
    let topLevel =
        missingProperties
            ""
            [ "lotNumber"
              "divisionCode"
              "departmentCode"
              "sectionCode"
              "processCategory"
              "inspectionCategory"
              "manufacturingCategory"
              "details" ]
            root

    let lotNumber =
        match tryGetJsonProperty "lotNumber" root with
        | Some value -> missingProperties "lotNumber." [ "year"; "location"; "seq" ] value
        | None -> []

    let details =
        match tryGetJsonProperty "details" root with
        | Some value when value.ValueKind = JsonValueKind.Array ->
            value.EnumerateArray()
            |> Seq.indexed
            |> Seq.collect (fun (index, detail) ->
                missingProperties
                    (sprintf "details[%d]." index)
                    [ "itemCategory"
                      "productCategoryCode"
                      "lengthSpecLower"
                      "thicknessSpecLower"
                      "thicknessSpecUpper"
                      "qualityGrade"
                      "count"
                      "quantity" ]
                    detail)
            |> List.ofSeq
        | _ -> []

    topLevel @ lotNumber @ details

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
    if isNull (box dto) then
        // body が null リテラルのとき DTO ごと null で bind される (Schemathesis 検出)
        Error
            [ { Field = "body"
                Message = "request body is required" } ]
    else

        let nulFields =
            [ yield
                  ("lotNumber.location",
                   (if isNull (box dto.lotNumber) then
                        null
                    else
                        dto.lotNumber.location))
              if not (isNull dto.details) then
                  for i, d in Seq.indexed dto.details do
                      if not (isNull (box d)) then
                          yield (sprintf "details[%d].productCategoryCode" i, d.productCategoryCode)
                          yield (sprintf "details[%d].qualityGrade" i, d.qualityGrade)
                          yield (sprintf "details[%d].premiumCategory" i, d.premiumCategory)
                          yield (sprintf "details[%d].itemCategory" i, d.itemCategory)
                          yield (sprintf "details[%d].inspectionResultCategory" i, d.inspectionResultCategory) ]
            |> List.filter (fun (_, v) -> containsNul v)

        if not (List.isEmpty nulFields) then
            Error(
                nulFields
                |> List.map (fun (field, _) ->
                    { Field = field
                      Message = "must not contain NUL (U+0000)" })
            )
        else

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
