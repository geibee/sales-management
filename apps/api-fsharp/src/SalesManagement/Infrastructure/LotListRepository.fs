module SalesManagement.Infrastructure.LotListRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types

type LotListItem =
    { LotNumber: LotNumber
      Status: string
      ManufacturingCompletedDate: DateOnly option
      ShippingDeadlineDate: DateOnly option
      ShippedDate: DateOnly option
      DestinationItem: string option
      Version: int }

type LotListFilter =
    { Status: string option
      Limit: int
      Offset: int }

type LotListResult =
    { Items: LotListItem list
      Total: int
      Limit: int
      Offset: int }

let private readDateOnlyOrNull (rd: IDataReader) (col: string) : DateOnly option =
    let idx = rd.GetOrdinal col

    if rd.IsDBNull idx then
        None
    else
        Some(DateOnly.FromDateTime(rd.GetDateTime idx))

let private readStringOrNull (rd: IDataReader) (col: string) : string option =
    let idx = rd.GetOrdinal col
    if rd.IsDBNull idx then None else Some(rd.GetString idx)

let private mapLotListItem (rd: IDataReader) : LotListItem =
    { LotNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
          Location = rd.GetString(rd.GetOrdinal "lot_number_location")
          Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }
      Status = rd.GetString(rd.GetOrdinal "status")
      ManufacturingCompletedDate = readDateOnlyOrNull rd "manufacturing_completed_date"
      ShippingDeadlineDate = readDateOnlyOrNull rd "shipping_deadline_date"
      ShippedDate = readDateOnlyOrNull rd "shipped_date"
      DestinationItem = readStringOrNull rd "destination_item"
      Version = rd.GetInt32(rd.GetOrdinal "version") }

let private baseSelectSql =
    """
    SELECT lot_number_year, lot_number_location, lot_number_seq,
           status, manufacturing_completed_date, shipping_deadline_date,
           shipped_date, destination_item, version
      FROM lot
    """

let private countSelectSql = "SELECT COUNT(*) FROM lot"

let private orderBySql =
    " ORDER BY lot_number_year, lot_number_location, lot_number_seq"

let private pagingSql = " LIMIT @limit OFFSET @offset"

let private limitParams (filter: LotListFilter) : RawDbParams =
    [ "limit", SqlType.Int32 filter.Limit; "offset", SqlType.Int32 filter.Offset ]

let private queryItems (conn: NpgsqlConnection) (sql: string) (parameters: RawDbParams) : LotListItem list =
    conn
    |> Db.newCommand sql
    |> Db.setParams parameters
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.query mapLotListItem

let private queryTotal (conn: NpgsqlConnection) (sql: string) (parameters: RawDbParams) : int =
    conn
    |> Db.newCommand sql
    |> Db.setParams parameters
    |> Db.scalar (fun (v: obj) -> Convert.ToInt32 v)

let list (conn: NpgsqlConnection) (filter: LotListFilter) : LotListResult =
    let pageParams = limitParams filter

    let items, total =
        match filter.Status with
        | None ->
            let xs = queryItems conn (baseSelectSql + orderBySql + pagingSql) pageParams
            let t = queryTotal conn countSelectSql []
            xs, t
        | Some s ->
            let where = " WHERE status = @status"
            let statusParam = [ "status", SqlType.String s ]

            let xs =
                queryItems conn (baseSelectSql + where + orderBySql + pagingSql) (statusParam @ pageParams)

            let t = queryTotal conn (countSelectSql + where) statusParam
            xs, t

    { Items = items
      Total = total
      Limit = filter.Limit
      Offset = filter.Offset }
