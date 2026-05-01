module SalesManagement.Api.SalesCaseDetailRoutes

open System
open System.Collections.Generic
open System.Data
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Http
open Donald
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

let private mapLotKey (rd: IDataReader) : LotNumber =
    { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
      Location = rd.GetString(rd.GetOrdinal "lot_number_location")
      Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }

let private salesCaseKeyParams (n: SalesCaseNumber) : RawDbParams =
    [ "year", SqlType.Int32 n.Year
      "month", SqlType.Int32 n.Month
      "seq", SqlType.Int32 n.Seq ]

let private loadLotIdStrings (conn: NpgsqlConnection) (n: SalesCaseNumber) : string list =
    conn
    |> Db.newCommand
        """
        SELECT lot_number_year, lot_number_location, lot_number_seq
          FROM sales_case_lot
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
         ORDER BY lot_number_year, lot_number_location, lot_number_seq
        """
    |> Db.setParams (salesCaseKeyParams n)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.query mapLotKey
    |> List.map formatLotNumber

type private DetailHeader =
    { CaseType: string
      Status: string
      DivisionCode: int
      SalesDate: DateOnly
      Version: int
      ShippingInstructionDate: DateOnly option
      ShippingCompletedDate: DateOnly option }

let private mapDetailHeader (rd: IDataReader) : DetailHeader =
    let optDate (col: string) =
        let idx = rd.GetOrdinal col

        if rd.IsDBNull idx then
            None
        else
            Some(DateOnly.FromDateTime(rd.GetDateTime idx))

    { CaseType = rd.GetString(rd.GetOrdinal "case_type")
      Status = rd.GetString(rd.GetOrdinal "status")
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
      SalesDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "sales_date"))
      Version = rd.GetInt32(rd.GetOrdinal "version")
      ShippingInstructionDate = optDate "shipping_instruction_date"
      ShippingCompletedDate = optDate "shipping_completed_date" }

let private tryLoadDetailHeader (conn: NpgsqlConnection) (n: SalesCaseNumber) : DetailHeader option =
    conn
    |> Db.newCommand
        """
        SELECT case_type, status, division_code, sales_date, version,
               shipping_instruction_date, shipping_completed_date
          FROM sales_case
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
        """
    |> Db.setParams (salesCaseKeyParams n)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.querySingle mapDetailHeader

let private isoDate (d: DateOnly) : string = d.ToString("yyyy-MM-dd")

let private loadDirectAppraisal (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    let rows =
        conn
        |> Db.newCommand
            """
            SELECT appraisal_type, appraisal_date, delivery_date, sales_market,
                   tax_excluded_estimated_total
              FROM appraisal
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams n)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query (fun rd ->
            let dict = Dictionary<string, obj>()
            dict.["type"] <- rd.GetString(rd.GetOrdinal "appraisal_type")
            dict.["appraisalDate"] <- isoDate (DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "appraisal_date")))
            dict.["deliveryDate"] <- isoDate (DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "delivery_date")))
            dict.["salesMarket"] <- rd.GetString(rd.GetOrdinal "sales_market")
            dict.["taxExcludedEstimatedTotal"] <- box (rd.GetInt32(rd.GetOrdinal "tax_excluded_estimated_total"))
            dict)

    match rows with
    | [] -> null
    | head :: _ -> head :> obj

let private loadDirectContract (conn: NpgsqlConnection) (n: SalesCaseNumber) : obj =
    let rows =
        conn
        |> Db.newCommand
            """
            SELECT c.contract_date, c.person, c.customer_number,
                   c.tax_excluded_contract_amount, c.consumption_tax
              FROM contract c
              JOIN appraisal a
                ON a.appraisal_number_year = c.appraisal_number_year
               AND a.appraisal_number_month = c.appraisal_number_month
               AND a.appraisal_number_seq = c.appraisal_number_seq
             WHERE a.sales_case_number_year = @year
               AND a.sales_case_number_month = @month
               AND a.sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams n)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query (fun rd ->
            let dict = Dictionary<string, obj>()
            dict.["contractDate"] <- isoDate (DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "contract_date")))
            dict.["person"] <- rd.GetString(rd.GetOrdinal "person")
            dict.["customerNumber"] <- rd.GetString(rd.GetOrdinal "customer_number")
            dict.["taxExcludedContractAmount"] <- box (rd.GetInt32(rd.GetOrdinal "tax_excluded_contract_amount"))
            dict.["consumptionTax"] <- box (rd.GetInt32(rd.GetOrdinal "consumption_tax"))
            dict)

    match rows with
    | [] -> null
    | head :: _ -> head :> obj

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
    let rows =
        conn
        |> Db.newCommand
            """
            SELECT result_date, result_amount
              FROM consignment_result
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams n)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query (fun rd ->
            let dict = Dictionary<string, obj>()
            dict.["resultDate"] <- isoDate (DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "result_date")))
            dict.["resultAmount"] <- box (rd.GetInt32(rd.GetOrdinal "result_amount"))
            dict)

    match rows with
    | [] -> null
    | head :: _ -> head :> obj

let private addDirectFields
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (header: DetailHeader)
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

let private addReservationFields
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (_header: DetailHeader)
    (dict: Dictionary<string, obj>)
    : unit =
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
    (header: DetailHeader)
    : Dictionary<string, obj> =
    let dict = Dictionary<string, obj>()
    dict.["salesCaseNumber"] <- formatSalesCaseNumber n
    dict.["caseType"] <- header.CaseType
    dict.["status"] <- header.Status
    dict.["lots"] <- (loadLotIdStrings conn n |> List.toArray :> obj)
    dict.["divisionCode"] <- box header.DivisionCode
    dict.["salesDate"] <- isoDate header.SalesDate
    dict.["version"] <- box header.Version

    match header.CaseType with
    | "direct" -> addDirectFields conn n header dict
    | "reservation" -> addReservationFields conn n header dict
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

            match tryLoadDetailHeader conn n with
            | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
            | Some header ->
                let payload = buildDetailResponse conn n header
                return! writeDetailJson payload next ctx
    }
