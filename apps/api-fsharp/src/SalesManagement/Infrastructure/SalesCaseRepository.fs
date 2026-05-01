module SalesManagement.Infrastructure.SalesCaseRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes

type SalesCaseHeader =
    { SalesCaseNumber: SalesCaseNumber
      DivisionCode: int
      SalesDate: DateOnly
      CaseType: string
      Status: string
      AppraisalNumber: AppraisalNumber option
      ContractNumber: ContractNumber option }

let private dateParam (d: DateOnly option) : SqlType =
    match d with
    | Some date -> SqlType.AnsiString(date.ToString("yyyy-MM-dd"))
    | None -> SqlType.Null

let private optionString (s: string option) : SqlType =
    match s with
    | Some v -> SqlType.String v
    | None -> SqlType.Null

let private optionInt (i: int option) : SqlType =
    match i with
    | Some v -> SqlType.Int32 v
    | None -> SqlType.Null

let private optionDecimal (d: decimal option) : SqlType =
    match d with
    | Some v -> SqlType.Decimal v
    | None -> SqlType.Null

let private salesCaseKeyParams (n: SalesCaseNumber) : RawDbParams =
    [ "year", SqlType.Int32 n.Year
      "month", SqlType.Int32 n.Month
      "seq", SqlType.Int32 n.Seq ]

let nextSalesCaseSeq (conn: NpgsqlConnection) (year: int) (month: int) : int =
    conn
    |> Db.newCommand
        """
        SELECT COALESCE(MAX(sales_case_number_seq), 0) + 1
          FROM sales_case
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
        """
    |> Db.setParams [ "year", SqlType.Int32 year; "month", SqlType.Int32 month ]
    |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

let nextContractSeq (conn: NpgsqlConnection) (year: int) (month: int) : int =
    conn
    |> Db.newCommand
        """
        SELECT COALESCE(MAX(contract_number_seq), 0) + 1
          FROM contract
         WHERE contract_number_year = @year
           AND contract_number_month = @month
        """
    |> Db.setParams [ "year", SqlType.Int32 year; "month", SqlType.Int32 month ]
    |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

let private mapSalesCaseHeader (rd: IDataReader) : SalesCaseHeader =
    let appraisalNumber: AppraisalNumber option =
        if rd.IsDBNull(rd.GetOrdinal "appraisal_number_year") then
            None
        else
            Some
                { Year = rd.GetInt32(rd.GetOrdinal "appraisal_number_year")
                  Month = rd.GetInt32(rd.GetOrdinal "appraisal_number_month")
                  Seq = rd.GetInt32(rd.GetOrdinal "appraisal_number_seq") }

    let contractNumber: ContractNumber option =
        if rd.IsDBNull(rd.GetOrdinal "contract_number_year") then
            None
        else
            Some
                { Year = rd.GetInt32(rd.GetOrdinal "contract_number_year")
                  Month = rd.GetInt32(rd.GetOrdinal "contract_number_month")
                  Seq = rd.GetInt32(rd.GetOrdinal "contract_number_seq") }

    let salesCaseNumber: SalesCaseNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "sales_case_number_year")
          Month = rd.GetInt32(rd.GetOrdinal "sales_case_number_month")
          Seq = rd.GetInt32(rd.GetOrdinal "sales_case_number_seq") }

    { SalesCaseNumber = salesCaseNumber
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
      SalesDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "sales_date"))
      CaseType = rd.GetString(rd.GetOrdinal "case_type")
      Status = rd.GetString(rd.GetOrdinal "status")
      AppraisalNumber = appraisalNumber
      ContractNumber = contractNumber }

let tryFindHeader (conn: NpgsqlConnection) (n: SalesCaseNumber) : SalesCaseHeader option =
    conn
    |> Db.newCommand
        """
        SELECT sc.sales_case_number_year, sc.sales_case_number_month, sc.sales_case_number_seq,
               sc.division_code, sc.sales_date, sc.case_type, sc.status,
               a.appraisal_number_year, a.appraisal_number_month, a.appraisal_number_seq,
               c.contract_number_year, c.contract_number_month, c.contract_number_seq
          FROM sales_case sc
          LEFT JOIN appraisal a
            ON a.sales_case_number_year = sc.sales_case_number_year
           AND a.sales_case_number_month = sc.sales_case_number_month
           AND a.sales_case_number_seq = sc.sales_case_number_seq
          LEFT JOIN contract c
            ON c.appraisal_number_year = a.appraisal_number_year
           AND c.appraisal_number_month = a.appraisal_number_month
           AND c.appraisal_number_seq = a.appraisal_number_seq
         WHERE sc.sales_case_number_year = @year
           AND sc.sales_case_number_month = @month
           AND sc.sales_case_number_seq = @seq
        """
    |> Db.setParams (salesCaseKeyParams n)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.querySingle mapSalesCaseHeader

let private insertSalesCaseRow
    (tran: IDbTransaction)
    (caseType: string)
    (initialStatus: string)
    (common: SalesCaseCommon)
    : unit =
    let (DivisionCode division) = common.DivisionCode

    tran
    |> Db.newCommandForTransaction
        """
        INSERT INTO sales_case (
            sales_case_number_year, sales_case_number_month, sales_case_number_seq,
            division_code, sales_date, case_type, status
        ) VALUES (
            @year, @month, @seq, @division, @sales_date::date, @case_type, @status
        )
        """
    |> Db.setParams
        [ "year", SqlType.Int32 common.SalesCaseNumber.Year
          "month", SqlType.Int32 common.SalesCaseNumber.Month
          "seq", SqlType.Int32 common.SalesCaseNumber.Seq
          "division", SqlType.Int32 division
          "sales_date", SqlType.AnsiString(common.SalesDate.ToString("yyyy-MM-dd"))
          "case_type", SqlType.String caseType
          "status", SqlType.String initialStatus ]
    |> Db.exec

let private insertCaseLotRow (tran: IDbTransaction) (caseNumber: SalesCaseNumber) (lot: InventoryLot) : unit =
    let lotNumber = (InventoryLot.common lot).LotNumber

    tran
    |> Db.newCommandForTransaction
        """
        INSERT INTO sales_case_lot (
            sales_case_number_year, sales_case_number_month, sales_case_number_seq,
            lot_number_year, lot_number_location, lot_number_seq
        ) VALUES (
            @year, @month, @seq, @lot_year, @lot_location, @lot_seq
        )
        """
    |> Db.setParams
        [ "year", SqlType.Int32 caseNumber.Year
          "month", SqlType.Int32 caseNumber.Month
          "seq", SqlType.Int32 caseNumber.Seq
          "lot_year", SqlType.Int32 lotNumber.Year
          "lot_location", SqlType.String lotNumber.Location
          "lot_seq", SqlType.Int32 lotNumber.Seq ]
    |> Db.exec

let insertCommon (conn: NpgsqlConnection) (caseType: string) (initialStatus: string) (common: SalesCaseCommon) : unit =
    conn
    |> Db.batch (fun tran ->
        insertSalesCaseRow tran caseType initialStatus common

        common.Lots
        |> NonEmptyList.toList
        |> List.iter (insertCaseLotRow tran common.SalesCaseNumber))

let insertCase (conn: NpgsqlConnection) (case: BeforeAppraisalCase) : unit =
    insertCommon conn "direct" "before_appraisal" case.Common

let insertReservationCase (conn: NpgsqlConnection) (case: BeforeReservationCase) : unit =
    insertCommon conn "reservation" "before_reservation" case.Common

let insertConsignmentCase (conn: NpgsqlConnection) (case: BeforeConsignmentCase) : unit =
    insertCommon conn "consignment" "before_consignment" case.Common

let updateStatus (conn: NpgsqlConnection) (caseNumber: SalesCaseNumber) (status: string) : unit =
    conn
    |> Db.newCommand
        """
        UPDATE sales_case
           SET status = @status
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
        """
    |> Db.setParams (("status", SqlType.String status) :: salesCaseKeyParams caseNumber)
    |> Db.exec

let private formatCaseId (n: SalesCaseNumber) : string =
    sprintf "%d-%02d-%03d" n.Year n.Month n.Seq

let private addParam (cmd: NpgsqlCommand) (name: string) (value: obj) : unit =
    let p = cmd.CreateParameter()
    p.ParameterName <- name
    p.Value <- (if isNull value then box DBNull.Value else value)
    cmd.Parameters.Add p |> ignore

let bumpSalesCaseVersionTx
    (tx: NpgsqlTransaction)
    (caseNumber: SalesCaseNumber)
    (newStatus: string)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    use cmd = tx.Connection.CreateCommand()
    cmd.Transaction <- tx

    cmd.CommandText <-
        """
        UPDATE sales_case
           SET status = @status, version = version + 1
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
           AND version = @expected_version
        RETURNING version
        """

    addParam cmd "status" (box newStatus)
    addParam cmd "year" (box caseNumber.Year)
    addParam cmd "month" (box caseNumber.Month)
    addParam cmd "seq" (box caseNumber.Seq)
    addParam cmd "expected_version" (box expectedVersion)

    let result = cmd.ExecuteScalar()

    let newVersion =
        if isNull result || (result :? DBNull) then
            0
        else
            Convert.ToInt32 result

    if newVersion <= 0 then
        Error(SalesManagement.Domain.Errors.OptimisticLockConflict("SalesCase", formatCaseId caseNumber))
    else
        Ok newVersion

let private bumpSalesCaseWithDateTx
    (tx: NpgsqlTransaction)
    (caseNumber: SalesCaseNumber)
    (newStatus: string)
    (dateColumn: string)
    (date: DateOnly option)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    use cmd = tx.Connection.CreateCommand()
    cmd.Transaction <- tx

    let setDate =
        match date with
        | Some _ -> sprintf "%s = @date::date" dateColumn
        | None -> sprintf "%s = NULL" dateColumn

    cmd.CommandText <-
        sprintf
            """
            UPDATE sales_case
               SET status = @status,
                   %s,
                   version = version + 1
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
               AND version = @expected_version
            RETURNING version
            """
            setDate

    addParam cmd "status" (box newStatus)
    addParam cmd "year" (box caseNumber.Year)
    addParam cmd "month" (box caseNumber.Month)
    addParam cmd "seq" (box caseNumber.Seq)
    addParam cmd "expected_version" (box expectedVersion)

    match date with
    | Some d -> addParam cmd "date" (box (d.ToString("yyyy-MM-dd")))
    | None -> ()

    let result = cmd.ExecuteScalar()

    let newVersion =
        if isNull result || (result :? DBNull) then
            0
        else
            Convert.ToInt32 result

    if newVersion <= 0 then
        Error(SalesManagement.Domain.Errors.OptimisticLockConflict("SalesCase", formatCaseId caseNumber))
    else
        Ok newVersion

let deleteCase (conn: NpgsqlConnection) (n: SalesCaseNumber) : unit =
    conn
    |> Db.batch (fun tran ->
        tran
        |> Db.newCommandForTransaction
            """
            DELETE FROM sales_case_lot
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams n)
        |> Db.exec

        tran
        |> Db.newCommandForTransaction
            """
            DELETE FROM sales_case
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams n)
        |> Db.exec)

let private mapLotKey (rd: IDataReader) : LotNumber =
    { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
      Location = rd.GetString(rd.GetOrdinal "lot_number_location")
      Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }

let private resolveManufacturedLot (conn: NpgsqlConnection) (lotNumber: LotNumber) : Result<ManufacturedLot, string> =
    match LotRepository.load conn lotNumber with
    | Error e -> Error e
    | Ok None -> Error(sprintf "Lot %s not found" (LotNumber.toString lotNumber))
    | Ok(Some(Manufactured lot)) -> Ok lot
    | Ok(Some _) -> Error(sprintf "Lot %s is not in manufactured state" (LotNumber.toString lotNumber))

let loadCaseLots (conn: NpgsqlConnection) (caseNumber: SalesCaseNumber) : Result<ManufacturedLot list, string> =
    let lotNumbers =
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
        |> Db.setParams (salesCaseKeyParams caseNumber)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query mapLotKey

    let folded =
        lotNumbers
        |> List.map (resolveManufacturedLot conn)
        |> List.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | Ok _, Error e -> Error e
                | Ok xs, Ok lot -> Ok(lot :: xs))
            (Ok [])

    folded |> Result.map List.rev

let private contractParams
    (number: ContractNumber)
    (appraisalNumber: AppraisalNumber)
    (contract: SalesContract)
    : RawDbParams =
    [ "year", SqlType.Int32 number.Year
      "month", SqlType.Int32 number.Month
      "seq", SqlType.Int32 number.Seq
      "appraisal_year", SqlType.Int32 appraisalNumber.Year
      "appraisal_month", SqlType.Int32 appraisalNumber.Month
      "appraisal_seq", SqlType.Int32 appraisalNumber.Seq
      "contract_date", SqlType.AnsiString(contract.ContractDate.ToString("yyyy-MM-dd"))
      "person", SqlType.String contract.Person
      "customer_number", SqlType.String contract.Buyer.CustomerNumber
      "agent_name", optionString contract.Buyer.AgentName
      "sales_type", SqlType.Int32 contract.SalesInfo.SalesType
      "item", SqlType.String contract.SalesInfo.Item
      "delivery_method", SqlType.String contract.SalesInfo.DeliveryMethod
      "sales_method", SqlType.Int32 contract.SalesInfo.SalesMethod
      "payment_deferral_condition", optionString contract.SalesInfo.PaymentDeferralCondition
      "payment_deferral_amount", optionInt (contract.SalesInfo.PaymentDeferralAmount |> Option.map Amount.value)
      "usage_", optionString contract.SalesInfo.Usage
      "tax_excluded_contract_amount", SqlType.Int32(Amount.value contract.SalesPriceInfo.TaxExcludedContractAmount)
      "consumption_tax", SqlType.Int32(Amount.value contract.SalesPriceInfo.ConsumptionTax)
      "tax_excluded_payment_amount", SqlType.Int32(Amount.value contract.SalesPriceInfo.TaxExcludedPaymentAmount)
      "payment_consumption_tax", SqlType.Int32(Amount.value contract.SalesPriceInfo.PaymentConsumptionTax) ]

let private runInTx
    (conn: NpgsqlConnection)
    (action: NpgsqlTransaction -> Result<int, SalesManagement.Domain.Errors.DomainError>)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    use tx = conn.BeginTransaction()

    try
        match action tx with
        | Error e ->
            tx.Rollback()
            Error e
        | Ok v ->
            tx.Commit()
            Ok v
    with ex ->
        try
            tx.Rollback()
        with _ ->
            ()

        reraise ()

let insertContract
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (contractNumber: ContractNumber)
    (contract: SalesContract)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            INSERT INTO contract (
                contract_number_year, contract_number_month, contract_number_seq,
                appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                contract_date, person, customer_number, agent_name,
                sales_type, item, delivery_method, sales_method,
                payment_deferral_condition, payment_deferral_amount, usage_,
                tax_excluded_contract_amount, consumption_tax,
                tax_excluded_payment_amount, payment_consumption_tax
            ) VALUES (
                @year, @month, @seq,
                @appraisal_year, @appraisal_month, @appraisal_seq,
                @contract_date::date, @person, @customer_number, @agent_name,
                @sales_type, @item, @delivery_method, @sales_method,
                @payment_deferral_condition, @payment_deferral_amount, @usage_,
                @tax_excluded_contract_amount, @consumption_tax,
                @tax_excluded_payment_amount, @payment_consumption_tax
            )
            """
        |> Db.setParams (contractParams contractNumber appraisalNumber contract)
        |> Db.exec

        bumpSalesCaseVersionTx tx caseNumber "contracted" expectedVersion)

let deleteContract
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (contractNumber: ContractNumber)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            DELETE FROM contract
             WHERE contract_number_year = @year
               AND contract_number_month = @month
               AND contract_number_seq = @seq
            """
        |> Db.setParams
            [ "year", SqlType.Int32 contractNumber.Year
              "month", SqlType.Int32 contractNumber.Month
              "seq", SqlType.Int32 contractNumber.Seq ]
        |> Db.exec

        bumpSalesCaseVersionTx tx caseNumber "appraised" expectedVersion)

let setShippingInstruction
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (date: DateOnly)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        bumpSalesCaseWithDateTx
            tx
            caseNumber
            "shipping_instructed"
            "shipping_instruction_date"
            (Some date)
            expectedVersion)

let clearShippingInstruction
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        bumpSalesCaseWithDateTx tx caseNumber "contracted" "shipping_instruction_date" None expectedVersion)

let setShippingCompletion
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (date: DateOnly)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        bumpSalesCaseWithDateTx
            tx
            caseNumber
            "shipping_completed"
            "shipping_completed_date"
            (Some date)
            expectedVersion)
