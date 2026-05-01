module SalesManagement.Infrastructure.SalesCaseListRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

type SalesCaseListItem =
    { SalesCaseNumber: SalesCaseNumber
      DivisionCode: int
      SalesDate: DateOnly
      CaseType: string
      Status: string }

type SalesCaseListFilter =
    { Status: string option
      CaseType: string option
      Limit: int
      Offset: int }

type SalesCaseListResult =
    { Items: SalesCaseListItem list
      Total: int
      Limit: int
      Offset: int }

let private mapSalesCaseListItem (rd: IDataReader) : SalesCaseListItem =
    { SalesCaseNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "sales_case_number_year")
          Month = rd.GetInt32(rd.GetOrdinal "sales_case_number_month")
          Seq = rd.GetInt32(rd.GetOrdinal "sales_case_number_seq") }
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
      SalesDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "sales_date"))
      CaseType = rd.GetString(rd.GetOrdinal "case_type")
      Status = rd.GetString(rd.GetOrdinal "status") }

let list (conn: NpgsqlConnection) (filter: SalesCaseListFilter) : SalesCaseListResult =
    let baseSelect =
        """
        SELECT sales_case_number_year, sales_case_number_month, sales_case_number_seq,
               division_code, sales_date, case_type, status
          FROM sales_case
        """

    let countSelect = "SELECT COUNT(*) FROM sales_case"

    let orderBy =
        " ORDER BY sales_case_number_year, sales_case_number_month, sales_case_number_seq"

    let pagingClause = " LIMIT @limit OFFSET @offset"

    let limitParams =
        [ "limit", SqlType.Int32 filter.Limit; "offset", SqlType.Int32 filter.Offset ]

    let conditions = System.Collections.Generic.List<string>()
    let extraParams = System.Collections.Generic.List<string * SqlType>()

    match filter.Status with
    | Some s ->
        conditions.Add "status = @status"
        extraParams.Add("status", SqlType.String s)
    | None -> ()

    match filter.CaseType with
    | Some s ->
        conditions.Add "case_type = @case_type"
        extraParams.Add("case_type", SqlType.String s)
    | None -> ()

    let where =
        if conditions.Count = 0 then
            ""
        else
            " WHERE " + String.Join(" AND ", conditions)

    let extras = extraParams |> List.ofSeq

    let items =
        conn
        |> Db.newCommand (baseSelect + where + orderBy + pagingClause)
        |> Db.setParams (extras @ limitParams)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query mapSalesCaseListItem

    let total =
        conn
        |> Db.newCommand (countSelect + where)
        |> Db.setParams extras
        |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

    { Items = items
      Total = total
      Limit = filter.Limit
      Offset = filter.Offset }
