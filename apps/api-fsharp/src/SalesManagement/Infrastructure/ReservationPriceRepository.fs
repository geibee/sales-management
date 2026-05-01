module SalesManagement.Infrastructure.ReservationPriceRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes

let private salesCaseKeyParams (n: SalesCaseNumber) : RawDbParams =
    [ "year", SqlType.Int32 n.Year
      "month", SqlType.Int32 n.Month
      "seq", SqlType.Int32 n.Seq ]

let private appraisalKeyParams (n: AppraisalNumber) : RawDbParams =
    [ "appraisal_year", SqlType.Int32 n.Year
      "appraisal_month", SqlType.Int32 n.Month
      "appraisal_seq", SqlType.Int32 n.Seq ]

let nextReservationPriceSeq (conn: NpgsqlConnection) (year: int) (month: int) : int =
    conn
    |> Db.newCommand
        """
        SELECT COALESCE(MAX(appraisal_number_seq), 0) + 1
          FROM reservation_price
         WHERE appraisal_number_year = @year
           AND appraisal_number_month = @month
        """
    |> Db.setParams [ "year", SqlType.Int32 year; "month", SqlType.Int32 month ]
    |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

type ReservationPriceRow =
    { AppraisalNumber: AppraisalNumber
      AppraisalDate: DateOnly
      ReservedLotInfo: string
      ReservedAmount: int
      Status: string
      DeterminedDate: DateOnly option
      DeterminedAmount: int option
      DeliveredDate: DateOnly option }

let private mapReservationRow (rd: IDataReader) : ReservationPriceRow =
    let nullableDate (column: string) : DateOnly option =
        let idx = rd.GetOrdinal column

        if rd.IsDBNull idx then
            None
        else
            Some(DateOnly.FromDateTime(rd.GetDateTime idx))

    let determinedAmount =
        let idx = rd.GetOrdinal "determined_amount"
        if rd.IsDBNull idx then None else Some(rd.GetInt32 idx)

    { AppraisalNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "appraisal_number_year")
          Month = rd.GetInt32(rd.GetOrdinal "appraisal_number_month")
          Seq = rd.GetInt32(rd.GetOrdinal "appraisal_number_seq") }
      AppraisalDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "appraisal_date"))
      ReservedLotInfo = rd.GetString(rd.GetOrdinal "reserved_lot_info")
      ReservedAmount = rd.GetInt32(rd.GetOrdinal "reserved_amount")
      Status = rd.GetString(rd.GetOrdinal "status")
      DeterminedDate = nullableDate "determined_date"
      DeterminedAmount = determinedAmount
      DeliveredDate = nullableDate "delivered_date" }

let tryFindByCase (conn: NpgsqlConnection) (caseNumber: SalesCaseNumber) : ReservationPriceRow option =
    conn
    |> Db.newCommand
        """
        SELECT appraisal_number_year, appraisal_number_month, appraisal_number_seq,
               appraisal_date, reserved_lot_info, reserved_amount,
               status, determined_date, determined_amount, delivered_date
          FROM reservation_price
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
        """
    |> Db.setParams (salesCaseKeyParams caseNumber)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.querySingle mapReservationRow

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

let insertProvisionalReservationPrice
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (common: ReservationPriceCommon)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            INSERT INTO reservation_price (
                appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                appraisal_date, reserved_lot_info, reserved_amount, status
            ) VALUES (
                @appraisal_year, @appraisal_month, @appraisal_seq,
                @year, @month, @seq,
                @appraisal_date::date, @reserved_lot_info, @reserved_amount, 'provisional'
            )
            """
        |> Db.setParams (
            (salesCaseKeyParams caseNumber)
            @ (appraisalKeyParams appraisalNumber)
            @ [ "appraisal_date", SqlType.AnsiString(common.AppraisalDate.ToString("yyyy-MM-dd"))
                "reserved_lot_info", SqlType.String common.ReservedLotInfo
                "reserved_amount", SqlType.Int32(Amount.value common.ReservedAmount) ]
        )
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "reserved" expectedVersion)

let setConfirmed
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (date: DateOnly)
    (amount: Amount)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            UPDATE reservation_price
               SET status = 'determined',
                   determined_date = @determined_date::date,
                   determined_amount = @determined_amount
             WHERE appraisal_number_year = @appraisal_year
               AND appraisal_number_month = @appraisal_month
               AND appraisal_number_seq = @appraisal_seq
            """
        |> Db.setParams (
            (appraisalKeyParams appraisalNumber)
            @ [ "determined_date", SqlType.AnsiString(date.ToString("yyyy-MM-dd"))
                "determined_amount", SqlType.Int32(Amount.value amount) ]
        )
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "reservation_confirmed" expectedVersion)

let clearConfirmed
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            UPDATE reservation_price
               SET status = 'provisional',
                   determined_date = NULL,
                   determined_amount = NULL
             WHERE appraisal_number_year = @appraisal_year
               AND appraisal_number_month = @appraisal_month
               AND appraisal_number_seq = @appraisal_seq
            """
        |> Db.setParams (appraisalKeyParams appraisalNumber)
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "reserved" expectedVersion)

let setDelivered
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (deliveredDate: DateOnly)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            UPDATE reservation_price
               SET delivered_date = @delivered_date::date
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (
            (salesCaseKeyParams caseNumber)
            @ [ "delivered_date", SqlType.AnsiString(deliveredDate.ToString("yyyy-MM-dd")) ]
        )
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "reservation_delivered" expectedVersion)
