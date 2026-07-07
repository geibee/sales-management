module SalesManagement.Api.SalesCaseDetailRoutes

// 販売案件詳細 (GET /sales-cases/{id})。SQL は Infrastructure 層
// (SalesCaseDetailRepository) に置き、本モジュールはレスポンス整形のみ担う
// (Api 層の SQL 禁止は Architecture/SourceRuleTests が強制。issue #9 Tier2-18)。

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Infrastructure
open SalesManagement.Api.SalesCaseDtos
open SalesManagement.Api.ProblemDetails

let private detailJsonOptions =
    JsonSerializerOptions(
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    )

let private writeDetailJson (payload: obj) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        ctx.Response.ContentType <- "application/json; charset=utf-8"
        let body = JsonSerializer.Serialize(payload, detailJsonOptions)
        do! ctx.Response.WriteAsync(body)
        return Some ctx
    }

let private formatLotNumber (lot: LotNumber) : string =
    sprintf "%d-%s-%03d" lot.Year lot.Location lot.Seq

let private isoDate (d: DateOnly) : string = d.ToString("yyyy-MM-dd")

let private loadDirectAppraisal (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    match SalesCaseDetailRepository.tryFindDirectAppraisal conn n with
    | None -> null
    | Some row ->
        let dict = Dictionary<string, obj>()
        dict.["type"] <- row.AppraisalType
        dict.["appraisalDate"] <- isoDate row.AppraisalDate
        dict.["deliveryDate"] <- isoDate row.DeliveryDate
        dict.["salesMarket"] <- row.SalesMarket
        dict.["taxExcludedEstimatedTotal"] <- box row.TaxExcludedEstimatedTotal
        dict :> obj

let private loadDirectContract (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    match SalesCaseDetailRepository.tryFindDirectContract conn n with
    | None -> null
    | Some row ->
        let dict = Dictionary<string, obj>()
        dict.["contractDate"] <- isoDate row.ContractDate
        dict.["person"] <- row.Person
        dict.["customerNumber"] <- row.CustomerNumber
        dict.["taxExcludedContractAmount"] <- box row.TaxExcludedContractAmount
        dict.["consumptionTax"] <- box row.ConsumptionTax
        dict :> obj

let private loadReservationPriceForDetail (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj * obj * obj =
    match ReservationPriceRepository.tryFindByCase conn n with
    | None -> null, null, null
    | Some row ->
        let appraisalDict = Dictionary<string, obj>()
        appraisalDict.["appraisalDate"] <- isoDate row.AppraisalDate
        appraisalDict.["reservedLotInfo"] <- row.ReservedLotInfo
        appraisalDict.["reservedAmount"] <- box row.ReservedAmount

        let determinationDict =
            match row.DeterminedDate, row.DeterminedAmount with
            | Some d, Some a ->
                let dict = Dictionary<string, obj>()
                dict.["determinedDate"] <- isoDate d
                dict.["determinedAmount"] <- box a
                dict :> obj
            | _ -> null

        let deliveryDict =
            match row.DeliveredDate with
            | Some d ->
                let dict = Dictionary<string, obj>()
                dict.["deliveredDate"] <- isoDate d
                dict :> obj
            | None -> null

        appraisalDict :> obj, determinationDict, deliveryDict

let private loadConsignor (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    match ConsignmentRepository.tryFindConsignorInfo conn n with
    | None -> null
    | Some info ->
        let dict = Dictionary<string, obj>()
        dict.["consignorName"] <- info.ConsignorName
        dict.["consignorCode"] <- info.ConsignorCode
        dict.["designatedDate"] <- isoDate info.DesignatedDate
        dict :> obj

let private loadConsignmentResult (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    match SalesCaseDetailRepository.tryFindConsignmentResult conn n with
    | None -> null
    | Some row ->
        let dict = Dictionary<string, obj>()
        dict.["resultDate"] <- isoDate row.ResultDate
        dict.["resultAmount"] <- box row.ResultAmount
        dict :> obj

let private addDirectFields
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (header: SalesCaseDetailRepository.DetailHeader)
    (dict: Dictionary<string, obj>)
    : unit =
    dict.["appraisal"] <- loadDirectAppraisal conn n
    dict.["contract"] <- loadDirectContract conn n

    dict.["shippingInstruction"] <-
        match header.ShippingInstructionDate with
        | None -> null
        | Some d ->
            let inner = Dictionary<string, obj>()
            inner.["instructionDate"] <- isoDate d
            inner :> obj

    dict.["shippingCompletion"] <-
        match header.ShippingCompletedDate with
        | None -> null
        | Some d ->
            let inner = Dictionary<string, obj>()
            inner.["completionDate"] <- isoDate d
            inner :> obj

let private addReservationFields (conn: NpgsqlConnection) (n: SalesCaseNumber) (dict: Dictionary<string, obj>) : unit =
    let appraisal, determination, delivery = loadReservationPriceForDetail conn n
    dict.["reservationPrice"] <- appraisal
    dict.["determination"] <- determination
    dict.["delivery"] <- delivery

let private addConsignmentFields (conn: NpgsqlConnection) (n: SalesCaseNumber) (dict: Dictionary<string, obj>) : unit =
    dict.["consignor"] <- loadConsignor conn n
    dict.["result"] <- loadConsignmentResult conn n

let private buildDetailResponse
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (header: SalesCaseDetailRepository.DetailHeader)
    : Dictionary<string, obj> =
    let dict = Dictionary<string, obj>()
    dict.["salesCaseNumber"] <- formatSalesCaseNumber n
    dict.["caseType"] <- header.CaseType
    dict.["status"] <- header.Status

    dict.["lots"] <-
        (SalesCaseDetailRepository.listLotNumbers conn n
         |> List.map formatLotNumber
         |> List.toArray
        :> obj)

    dict.["divisionCode"] <- box header.DivisionCode
    dict.["salesDate"] <- isoDate header.SalesDate
    dict.["version"] <- box header.Version

    match header.CaseType with
    | "direct" -> addDirectFields conn n header dict
    | "reservation" -> addReservationFields conn n dict
    | "consignment" -> addConsignmentFields conn n dict
    | _ -> ()

    dict

let getSalesCaseHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case number" next ctx
        | Some n ->
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()

            match SalesCaseDetailRepository.tryFindHeader conn n with
            | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
            | Some header ->
                let payload = buildDetailResponse conn n header
                return! writeDetailJson payload next ctx
    }
