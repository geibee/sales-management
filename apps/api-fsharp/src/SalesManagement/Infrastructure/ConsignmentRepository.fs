module SalesManagement.Infrastructure.ConsignmentRepository

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

let private mapConsignorInfo (rd: IDataReader) : ConsignorInfo =
    { ConsignorName = rd.GetString(rd.GetOrdinal "consignor_name")
      ConsignorCode = rd.GetString(rd.GetOrdinal "consignor_code")
      DesignatedDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "designated_date")) }

let tryFindConsignorInfo (conn: NpgsqlConnection) (caseNumber: SalesCaseNumber) : ConsignorInfo option =
    conn
    |> Db.newCommand
        """
        SELECT consignor_name, consignor_code, designated_date
          FROM consignment_info
         WHERE sales_case_number_year = @year
           AND sales_case_number_month = @month
           AND sales_case_number_seq = @seq
        """
    |> Db.setParams (salesCaseKeyParams caseNumber)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.querySingle mapConsignorInfo

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

let insertConsignmentInfo
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (info: ConsignorInfo)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            INSERT INTO consignment_info (
                sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                consignor_name, consignor_code, designated_date
            ) VALUES (
                @year, @month, @seq,
                @consignor_name, @consignor_code, @designated_date::date
            )
            """
        |> Db.setParams (
            (salesCaseKeyParams caseNumber)
            @ [ "consignor_name", SqlType.String info.ConsignorName
                "consignor_code", SqlType.String info.ConsignorCode
                "designated_date", SqlType.AnsiString(info.DesignatedDate.ToString("yyyy-MM-dd")) ]
        )
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "consignment_designated" expectedVersion)

let deleteConsignmentInfo
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            DELETE FROM consignment_info
             WHERE sales_case_number_year = @year
               AND sales_case_number_month = @month
               AND sales_case_number_seq = @seq
            """
        |> Db.setParams (salesCaseKeyParams caseNumber)
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "before_consignment" expectedVersion)

let insertConsignmentResult
    (conn: NpgsqlConnection)
    (caseNumber: SalesCaseNumber)
    (result: ConsignmentResult)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    runInTx conn (fun tx ->
        let tran = tx :> IDbTransaction

        tran
        |> Db.newCommandForTransaction
            """
            INSERT INTO consignment_result (
                sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                result_date, result_amount
            ) VALUES (
                @year, @month, @seq, @result_date::date, @result_amount
            )
            """
        |> Db.setParams (
            (salesCaseKeyParams caseNumber)
            @ [ "result_date", SqlType.AnsiString(result.ResultDate.ToString("yyyy-MM-dd"))
                "result_amount", SqlType.Int32(Amount.value result.ResultAmount) ]
        )
        |> Db.exec

        SalesCaseRepository.bumpSalesCaseVersionTx tx caseNumber "consignment_result_entered" expectedVersion)
