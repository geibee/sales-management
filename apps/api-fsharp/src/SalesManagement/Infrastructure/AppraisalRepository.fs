module SalesManagement.Infrastructure.AppraisalRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

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

let private optionAmount (a: Amount option) : SqlType =
    optionInt (a |> Option.map Amount.value)

let private salesCaseKeyParams (n: SalesCaseNumber) : RawDbParams =
    [ "year", SqlType.Int32 n.Year
      "month", SqlType.Int32 n.Month
      "seq", SqlType.Int32 n.Seq ]

let private appraisalKeyParams (n: AppraisalNumber) : RawDbParams =
    [ "appraisal_year", SqlType.Int32 n.Year
      "appraisal_month", SqlType.Int32 n.Month
      "appraisal_seq", SqlType.Int32 n.Seq ]

let nextAppraisalSeq (conn: NpgsqlConnection) (year: int) (month: int) : int =
    conn
    |> Db.newCommand
        """
        SELECT COALESCE(MAX(appraisal_number_seq), 0) + 1
          FROM appraisal
         WHERE appraisal_number_year = @year
           AND appraisal_number_month = @month
        """
    |> Db.setParams [ "year", SqlType.Int32 year; "month", SqlType.Int32 month ]
    |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

let private appraisalCommonOf (appraisal: PriceAppraisal) : AppraisalCommon =
    match appraisal with
    | Normal a -> a.Common
    | CustomerContract a -> a.Common

let private appraisalParams
    (caseNumber: SalesCaseNumber)
    (number: AppraisalNumber)
    (appraisal: PriceAppraisal)
    : RawDbParams =
    let common = appraisalCommonOf appraisal

    let kind, customerContractNumber, contractAdjustmentRate =
        match appraisal with
        | Normal _ -> "normal", None, None
        | CustomerContract a -> "customer_contract", Some a.CustomerContractNumber, Some a.ContractAdjustmentRate

    [ "appraisal_year", SqlType.Int32 number.Year
      "appraisal_month", SqlType.Int32 number.Month
      "appraisal_seq", SqlType.Int32 number.Seq
      "case_year", SqlType.Int32 caseNumber.Year
      "case_month", SqlType.Int32 caseNumber.Month
      "case_seq", SqlType.Int32 caseNumber.Seq
      "appraisal_type", SqlType.String kind
      "appraisal_date", SqlType.AnsiString(common.AppraisalDate.ToString("yyyy-MM-dd"))
      "delivery_date", SqlType.AnsiString(common.DeliveryDate.ToString("yyyy-MM-dd"))
      "sales_market", SqlType.String common.SalesMarket
      "base_unit_price_date", SqlType.String common.BaseUnitPriceDate
      "period_adjustment_rate_date", SqlType.String common.PeriodAdjustmentRateDate
      "counterparty_adjustment_rate_date", SqlType.String common.CounterpartyAdjustmentRateDate
      "tax_excluded_estimated_total", SqlType.Int32(Amount.value common.TaxExcludedEstimatedTotal)
      "customer_contract_number", optionString customerContractNumber
      "contract_adjustment_rate", optionDecimal contractAdjustmentRate ]

let private lotAppraisalParams (number: AppraisalNumber) (la: LotAppraisal) : RawDbParams =
    [ "appraisal_year", SqlType.Int32 number.Year
      "appraisal_month", SqlType.Int32 number.Month
      "appraisal_seq", SqlType.Int32 number.Seq
      "lot_year", SqlType.Int32 la.LotNumber.Year
      "lot_location", SqlType.String la.LotNumber.Location
      "lot_seq", SqlType.Int32 la.LotNumber.Seq
      "processing_cost", optionAmount la.ProcessingCost
      "individual_order_premium", optionDecimal la.IndividualOrderPremium
      "grade_premium", optionDecimal la.GradePremium
      "reservation_addon", optionDecimal la.ReservationAddon
      "adjustment_rate", optionDecimal la.AdjustmentRate
      "quality_adjustment_rate", optionDecimal la.QualityAdjustmentRate
      "manufacturing_cost_unit_price", optionAmount la.ManufacturingUnitCost
      "expected_sales_period", optionInt la.ExpectedSalesPeriod
      "target_profit_rate", optionDecimal la.TargetProfitRate ]

let private lotDetailAppraisalParams
    (number: AppraisalNumber)
    (lotNumber: LotNumber)
    (seqNo: int)
    (lda: LotDetailAppraisal)
    : RawDbParams =
    [ "appraisal_year", SqlType.Int32 number.Year
      "appraisal_month", SqlType.Int32 number.Month
      "appraisal_seq", SqlType.Int32 number.Seq
      "lot_year", SqlType.Int32 lotNumber.Year
      "lot_location", SqlType.String lotNumber.Location
      "lot_seq", SqlType.Int32 lotNumber.Seq
      "detail_seq_no", SqlType.Int32 seqNo
      "base_unit_price", SqlType.Int32(Amount.value lda.BaseUnitPrice)
      "period_adjustment_rate", SqlType.Decimal lda.PeriodAdjustmentRate
      "counterparty_adjustment_rate", SqlType.Decimal lda.CounterpartyAdjustmentRate
      "exceptional_period_adjustment_rate", optionDecimal lda.ExceptionalPeriodAdjustmentRate ]

let private insertLotAppraisalRow (tran: IDbTransaction) (number: AppraisalNumber) (la: LotAppraisal) : unit =
    tran
    |> Db.newCommandForTransaction
        """
        INSERT INTO lot_appraisal (
            appraisal_number_year, appraisal_number_month, appraisal_number_seq,
            lot_number_year, lot_number_location, lot_number_seq,
            processing_cost, individual_order_premium, grade_premium, reservation_addon,
            adjustment_rate, quality_adjustment_rate, manufacturing_cost_unit_price,
            expected_sales_period, target_profit_rate
        ) VALUES (
            @appraisal_year, @appraisal_month, @appraisal_seq,
            @lot_year, @lot_location, @lot_seq,
            @processing_cost, @individual_order_premium, @grade_premium, @reservation_addon,
            @adjustment_rate, @quality_adjustment_rate, @manufacturing_cost_unit_price,
            @expected_sales_period, @target_profit_rate
        )
        """
    |> Db.setParams (lotAppraisalParams number la)
    |> Db.exec

let private insertLotDetailAppraisalRow
    (tran: IDbTransaction)
    (number: AppraisalNumber)
    (lotNumber: LotNumber)
    (seqNo: int)
    (lda: LotDetailAppraisal)
    : unit =
    tran
    |> Db.newCommandForTransaction
        """
        INSERT INTO lot_detail_appraisal (
            appraisal_number_year, appraisal_number_month, appraisal_number_seq,
            lot_number_year, lot_number_location, lot_number_seq,
            detail_seq_no, base_unit_price, period_adjustment_rate, counterparty_adjustment_rate,
            exceptional_period_adjustment_rate
        ) VALUES (
            @appraisal_year, @appraisal_month, @appraisal_seq,
            @lot_year, @lot_location, @lot_seq,
            @detail_seq_no, @base_unit_price, @period_adjustment_rate, @counterparty_adjustment_rate,
            @exceptional_period_adjustment_rate
        )
        """
    |> Db.setParams (lotDetailAppraisalParams number lotNumber seqNo lda)
    |> Db.exec

let private insertAllChildren
    (tran: IDbTransaction)
    (number: AppraisalNumber)
    (lotAppraisals: NonEmptyList<LotAppraisal>)
    : unit =
    lotAppraisals
    |> NonEmptyList.toList
    |> List.iter (fun la ->
        insertLotAppraisalRow tran number la

        la.DetailAppraisals
        |> NonEmptyList.toList
        |> List.iteri (fun i d -> insertLotDetailAppraisalRow tran number la.LotNumber (i + 1) d))

let private deleteAllChildren (tran: IDbTransaction) (number: AppraisalNumber) : unit =
    tran
    |> Db.newCommandForTransaction
        """
        DELETE FROM lot_detail_appraisal
         WHERE appraisal_number_year = @appraisal_year
           AND appraisal_number_month = @appraisal_month
           AND appraisal_number_seq = @appraisal_seq
        """
    |> Db.setParams (appraisalKeyParams number)
    |> Db.exec

    tran
    |> Db.newCommandForTransaction
        """
        DELETE FROM lot_appraisal
         WHERE appraisal_number_year = @appraisal_year
           AND appraisal_number_month = @appraisal_month
           AND appraisal_number_seq = @appraisal_seq
        """
    |> Db.setParams (appraisalKeyParams number)
    |> Db.exec

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

let insertAppraisal
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (appraisal: PriceAppraisal)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    let common = appraisalCommonOf appraisal

    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            INSERT INTO appraisal (
                appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                appraisal_type, appraisal_date, delivery_date,
                sales_market, base_unit_price_date, period_adjustment_rate_date, counterparty_adjustment_rate_date,
                tax_excluded_estimated_total, customer_contract_number, contract_adjustment_rate
            ) VALUES (
                @appraisal_year, @appraisal_month, @appraisal_seq,
                @case_year, @case_month, @case_seq,
                @appraisal_type, @appraisal_date::date, @delivery_date::date,
                @sales_market, @base_unit_price_date, @period_adjustment_rate_date, @counterparty_adjustment_rate_date,
                @tax_excluded_estimated_total, @customer_contract_number, @contract_adjustment_rate
            )
            """
        |> Db.setParams (appraisalParams caseNumber appraisalNumber appraisal)
        |> Db.exec

        insertAllChildren tran appraisalNumber common.LotAppraisals

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "appraised" expectedVersion)

let updateAppraisal
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (appraisal: PriceAppraisal)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    let common = appraisalCommonOf appraisal

    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        deleteAllChildren tran appraisalNumber

        tran
        |> Db.newCommandForTransaction
            """
            UPDATE appraisal
               SET appraisal_type = @appraisal_type,
                   appraisal_date = @appraisal_date::date,
                   delivery_date = @delivery_date::date,
                   sales_market = @sales_market,
                   base_unit_price_date = @base_unit_price_date,
                   period_adjustment_rate_date = @period_adjustment_rate_date,
                   counterparty_adjustment_rate_date = @counterparty_adjustment_rate_date,
                   tax_excluded_estimated_total = @tax_excluded_estimated_total,
                   customer_contract_number = @customer_contract_number,
                   contract_adjustment_rate = @contract_adjustment_rate
             WHERE appraisal_number_year = @appraisal_year
               AND appraisal_number_month = @appraisal_month
               AND appraisal_number_seq = @appraisal_seq
            """
        |> Db.setParams (appraisalParams caseNumber appraisalNumber appraisal)
        |> Db.exec

        insertAllChildren tran appraisalNumber common.LotAppraisals

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "appraised" expectedVersion)

let deleteAppraisal
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        deleteAllChildren tran appraisalNumber

        tran
        |> Db.newCommandForTransaction
            """
            DELETE FROM appraisal
             WHERE appraisal_number_year = @appraisal_year
               AND appraisal_number_month = @appraisal_month
               AND appraisal_number_seq = @appraisal_seq
            """
        |> Db.setParams (appraisalKeyParams appraisalNumber)
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "before_appraisal" expectedVersion)
