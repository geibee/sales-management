module SalesManagement.Infrastructure.SalesCaseDetailRepository

// 販売案件詳細 (GET /sales-cases/{id}) の読み取りクエリ。
// SQL は Infrastructure 層に置く (Api 層の SQL 禁止は Architecture/SourceRuleTests が強制)。

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

let private salesCaseKeyParams (n: SalesCaseNumber) : RawDbParams =
    [ "year", SqlType.Int32 n.Year
      "month", SqlType.Int32 n.Month
      "seq", SqlType.Int32 n.Seq ]

let private dateOnly (rd: IDataReader) (column: string) : DateOnly =
    DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal column))

let private optionalDate (rd: IDataReader) (column: string) : DateOnly option =
    let idx = rd.GetOrdinal column

    if rd.IsDBNull idx then
        None
    else
        Some(DateOnly.FromDateTime(rd.GetDateTime idx))

type DetailHeader =
    { CaseType: string
      Status: string
      DivisionCode: int
      SalesDate: DateOnly
      Version: int
      ShippingInstructionDate: DateOnly option
      ShippingCompletedDate: DateOnly option }

type DirectAppraisalRow =
    { AppraisalType: string
      AppraisalDate: DateOnly
      DeliveryDate: DateOnly
      SalesMarket: string
      TaxExcludedEstimatedTotal: int }

type DirectContractRow =
    { ContractDate: DateOnly
      Person: string
      CustomerNumber: string
      TaxExcludedContractAmount: int
      ConsumptionTax: int }

type ConsignmentResultRow =
    { ResultDate: DateOnly
      ResultAmount: int }

let listLotNumbers (conn: NpgsqlConnection) (n: SalesCaseNumber) : LotNumber list =
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
    |> Db.query (fun rd ->
        { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
          Location = rd.GetString(rd.GetOrdinal "lot_number_location")
          Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") })

let tryFindHeader (conn: NpgsqlConnection) (n: SalesCaseNumber) : DetailHeader option =
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
    |> Db.querySingle (fun rd ->
        { CaseType = rd.GetString(rd.GetOrdinal "case_type")
          Status = rd.GetString(rd.GetOrdinal "status")
          DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
          SalesDate = dateOnly rd "sales_date"
          Version = rd.GetInt32(rd.GetOrdinal "version")
          ShippingInstructionDate = optionalDate rd "shipping_instruction_date"
          ShippingCompletedDate = optionalDate rd "shipping_completed_date" })

let tryFindDirectAppraisal (conn: NpgsqlConnection) (n: SalesCaseNumber) : DirectAppraisalRow option =
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
        { AppraisalType = rd.GetString(rd.GetOrdinal "appraisal_type")
          AppraisalDate = dateOnly rd "appraisal_date"
          DeliveryDate = dateOnly rd "delivery_date"
          SalesMarket = rd.GetString(rd.GetOrdinal "sales_market")
          TaxExcludedEstimatedTotal = rd.GetInt32(rd.GetOrdinal "tax_excluded_estimated_total") })
    |> List.tryHead

let tryFindDirectContract (conn: NpgsqlConnection) (n: SalesCaseNumber) : DirectContractRow option =
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
        { ContractDate = dateOnly rd "contract_date"
          Person = rd.GetString(rd.GetOrdinal "person")
          CustomerNumber = rd.GetString(rd.GetOrdinal "customer_number")
          TaxExcludedContractAmount = rd.GetInt32(rd.GetOrdinal "tax_excluded_contract_amount")
          ConsumptionTax = rd.GetInt32(rd.GetOrdinal "consumption_tax") })
    |> List.tryHead

let tryFindConsignmentResult (conn: NpgsqlConnection) (n: SalesCaseNumber) : ConsignmentResultRow option =
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
        { ResultDate = dateOnly rd "result_date"
          ResultAmount = rd.GetInt32(rd.GetOrdinal "result_amount") })
    |> List.tryHead
