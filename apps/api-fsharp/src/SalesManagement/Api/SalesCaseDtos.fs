module SalesManagement.Api.SalesCaseDtos

open System
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

[<CLIMutable>]
type CreateSalesCaseDto =
    { lots: string[]
      divisionCode: int
      salesDate: string
      caseType: string }

[<CLIMutable>]
type LotDetailAppraisalDto =
    { detailIndex: int
      baseUnitPrice: int
      periodAdjustmentRate: decimal
      counterpartyAdjustmentRate: decimal
      exceptionalPeriodAdjustmentRate: System.Nullable<decimal> }

[<CLIMutable>]
type LotAppraisalDto =
    { lotNumber: string
      detailAppraisals: LotDetailAppraisalDto[] }

[<CLIMutable>]
type CreateAppraisalDto =
    { ``type``: string
      appraisalDate: string
      deliveryDate: string
      salesMarket: string
      baseUnitPriceDate: string
      periodAdjustmentRateDate: string
      counterpartyAdjustmentRateDate: string
      taxExcludedEstimatedTotal: int
      customerContractNumber: string
      contractAdjustmentRate: System.Nullable<decimal>
      lotAppraisals: LotAppraisalDto[]
      version: System.Nullable<int> }

[<CLIMutable>]
type BuyerDto =
    { customerNumber: string
      agentName: string }

[<CLIMutable>]
type CreateContractDto =
    { contractDate: string
      person: string
      buyer: BuyerDto
      salesType: int
      item: string
      deliveryMethod: string
      paymentDeferralCondition: string
      salesMethod: int
      usage: string
      paymentDeferralAmount: System.Nullable<int>
      taxExcludedContractAmount: int
      consumptionTax: int
      taxExcludedPaymentAmount: int
      paymentConsumptionTax: int
      version: System.Nullable<int> }

[<CLIMutable>]
type DateOnlyDto =
    { date: string
      version: System.Nullable<int> }

[<CLIMutable>]
type VersionOnlyDto = { version: System.Nullable<int> }

type CreatedSalesCaseResponse =
    { salesCaseNumber: string
      status: string
      version: int }

let nullToOption (s: string) : string option =
    if String.IsNullOrEmpty s then None else Some s

let nullableToOption (n: System.Nullable<'a>) : 'a option =
    if n.HasValue then Some n.Value else None

let formatSalesCaseNumber (n: SalesCaseNumber) : string =
    sprintf "%d-%02d-%03d" n.Year n.Month n.Seq

let trySalesCaseNumber (s: string) : SalesCaseNumber option =
    let parts = s.Split('-')

    if parts.Length <> 3 then
        None
    else
        match System.Int32.TryParse parts.[0], System.Int32.TryParse parts.[1], System.Int32.TryParse parts.[2] with
        | (true, year), (true, month), (true, seq) ->
            Some
                { Year = year
                  Month = month
                  Seq = seq }
        | _ -> None

let parseDate (s: string) : DateOnly option =
    match DateOnly.TryParse s with
    | true, d -> Some d
    | _ -> None

let private dtoToDetailAppraisal
    (lot: ManufacturedLot)
    (dto: LotDetailAppraisalDto)
    : Result<LotDetailAppraisal, string> =
    match Amount.tryCreate dto.baseUnitPrice with
    | Error e -> Error e
    | Ok price ->
        let detail =
            lot.Common.Details |> NonEmptyList.toList |> List.tryItem (dto.detailIndex - 1)

        match detail with
        | None -> Error(sprintf "detailIndex %d out of range" dto.detailIndex)
        | Some d ->
            Ok
                { LotDetail = d
                  BaseUnitPrice = price
                  PeriodAdjustmentRate = dto.periodAdjustmentRate
                  CounterpartyAdjustmentRate = dto.counterpartyAdjustmentRate
                  ExceptionalPeriodAdjustmentRate = nullableToOption dto.exceptionalPeriodAdjustmentRate }

let private detailListToNel (xs: 'a list) : Result<NonEmptyList<'a>, string> =
    match NonEmptyList.ofList xs with
    | Some n -> Ok n
    | None -> Error "list must contain at least one element"

let private collapseResults (results: Result<'a, string> list) : Result<'a list, string> =
    let step (acc: Result<'a list, string>) (r: Result<'a, string>) =
        match acc, r with
        | Error e, _ -> Error e
        | Ok _, Error e -> Error e
        | Ok xs, Ok d -> Ok(d :: xs)

    results |> List.fold step (Ok []) |> Result.map List.rev

let private dtoToLotAppraisal (lot: ManufacturedLot) (dto: LotAppraisalDto) : Result<LotAppraisal, string> =
    let detailResults =
        dto.detailAppraisals |> Array.map (dtoToDetailAppraisal lot) |> Array.toList

    match collapseResults detailResults with
    | Error e -> Error e
    | Ok ordered ->
        match detailListToNel ordered with
        | Error e -> Error e
        | Ok nel ->
            Ok
                { LotNumber = lot.Common.LotNumber
                  ProcessingCost = None
                  IndividualOrderPremium = None
                  GradePremium = None
                  ReservationAddon = None
                  AdjustmentRate = None
                  QualityAdjustmentRate = None
                  ManufacturingUnitCost = None
                  ExpectedSalesPeriod = None
                  TargetProfitRate = None
                  DetailAppraisals = nel }

let private resolveLot (lots: ManufacturedLot list) (lotNumberStr: string) : Result<ManufacturedLot, string> =
    match LotNumber.tryParse lotNumberStr with
    | None -> Error(sprintf "Invalid lot number %s" lotNumberStr)
    | Some lotNumber ->
        match lots |> List.tryFind (fun l -> l.Common.LotNumber = lotNumber) with
        | None -> Error(sprintf "Lot %s not in case" lotNumberStr)
        | Some lot -> Ok lot

let private buildLotAppraisals
    (lots: ManufacturedLot list)
    (lotAppraisals: LotAppraisalDto[])
    : Result<NonEmptyList<LotAppraisal>, string> =
    let pairResults =
        lotAppraisals
        |> Array.toList
        |> List.map (fun la ->
            match resolveLot lots la.lotNumber with
            | Error e -> Error e
            | Ok lot -> dtoToLotAppraisal lot la)

    match collapseResults pairResults with
    | Error e -> Error e
    | Ok ordered -> detailListToNel ordered

let buildAppraisalCommon
    (dto: CreateAppraisalDto)
    (number: AppraisalNumber)
    (lots: ManufacturedLot list)
    : Result<AppraisalCommon, string> =
    match Amount.tryCreate dto.taxExcludedEstimatedTotal, parseDate dto.appraisalDate, parseDate dto.deliveryDate with
    | Error e, _, _ -> Error e
    | _, None, _ -> Error "appraisalDate must be ISO date"
    | _, _, None -> Error "deliveryDate must be ISO date"
    | Ok total, Some appraisalDate, Some deliveryDate ->
        match buildLotAppraisals lots dto.lotAppraisals with
        | Error e -> Error e
        | Ok lotAppraisals ->
            Ok
                { AppraisalNumber = number
                  AppraisalDate = appraisalDate
                  DeliveryDate = deliveryDate
                  SalesMarket = dto.salesMarket
                  BaseUnitPriceDate = dto.baseUnitPriceDate
                  PeriodAdjustmentRateDate = dto.periodAdjustmentRateDate
                  CounterpartyAdjustmentRateDate = dto.counterpartyAdjustmentRateDate
                  TaxExcludedEstimatedTotal = total
                  LotAppraisals = lotAppraisals }

let buildPriceAppraisal
    (dto: CreateAppraisalDto)
    (number: AppraisalNumber)
    (lots: ManufacturedLot list)
    : Result<PriceAppraisal, string> =
    match buildAppraisalCommon dto number lots with
    | Error e -> Error e
    | Ok common ->
        match dto.``type`` with
        | "normal" -> Ok(Normal { Common = common })
        | "customer_contract" ->
            match dto.customerContractNumber, nullableToOption dto.contractAdjustmentRate with
            | null, _
            | _, None -> Error "customer contract appraisal requires customerContractNumber and contractAdjustmentRate"
            | customerContractNumber, Some rate ->
                Ok(
                    CustomerContract
                        { Common = common
                          CustomerContractNumber = customerContractNumber
                          ContractAdjustmentRate = rate }
                )
        | other -> Error(sprintf "Unknown appraisal type: %s" other)

let private buildSalesPriceInfo (dto: CreateContractDto) : Result<SalesPriceInfo, string> =
    match
        Amount.tryCreate dto.taxExcludedContractAmount,
        Amount.tryCreate dto.consumptionTax,
        Amount.tryCreate dto.taxExcludedPaymentAmount,
        Amount.tryCreate dto.paymentConsumptionTax
    with
    | Error e, _, _, _
    | _, Error e, _, _
    | _, _, Error e, _
    | _, _, _, Error e -> Error e
    | Ok a1, Ok a2, Ok a3, Ok a4 ->
        Ok
            { TaxExcludedContractAmount = a1
              ConsumptionTax = a2
              TaxExcludedPaymentAmount = a3
              PaymentConsumptionTax = a4 }

let private buildSalesInfo (dto: CreateContractDto) : Result<SalesInfo, string> =
    let deferral =
        match nullableToOption dto.paymentDeferralAmount with
        | None -> Ok None
        | Some v -> Amount.tryCreate v |> Result.map Some

    match deferral with
    | Error e -> Error e
    | Ok paymentDeferralAmount ->
        Ok
            { SalesType = dto.salesType
              Item = dto.item
              DeliveryMethod = dto.deliveryMethod
              PaymentDeferralCondition = nullToOption dto.paymentDeferralCondition
              SalesMethod = dto.salesMethod
              Usage = nullToOption dto.usage
              PaymentDeferralAmount = paymentDeferralAmount }

let buildSalesContract
    (dto: CreateContractDto)
    (number: ContractNumber)
    (appraisalNumber: AppraisalNumber)
    : Result<SalesContract, string> =
    match parseDate dto.contractDate, buildSalesInfo dto, buildSalesPriceInfo dto with
    | None, _, _ -> Error "contractDate must be ISO date"
    | _, Error e, _ -> Error e
    | _, _, Error e -> Error e
    | Some date, Ok salesInfo, Ok salesPriceInfo ->
        Ok
            { ContractNumber = number
              ContractDate = date
              Person = dto.person
              Buyer =
                { CustomerNumber = dto.buyer.customerNumber
                  AgentName = nullToOption dto.buyer.agentName }
              SalesInfo = salesInfo
              SalesPriceInfo = salesPriceInfo
              AppraisalNumber = appraisalNumber }
