module SalesManagement.Infrastructure.LotRepository

open System
open System.Data
open Donald
open Npgsql
open SalesManagement.Domain.Types

let private dateParam (d: DateOnly option) : SqlType =
    match d with
    | Some date -> SqlType.AnsiString(date.ToString("yyyy-MM-dd"))
    | None -> SqlType.Null

let private detailParams (lotNumber: LotNumber) (seqNo: int) (detail: LotDetail) : RawDbParams =
    [ "year", SqlType.Int32 lotNumber.Year
      "location", SqlType.String lotNumber.Location
      "seq", SqlType.Int32 lotNumber.Seq
      "seq_no", SqlType.Int32 seqNo
      "item_category", SqlType.String(ItemCategory.toString detail.ItemCategory)
      "premium_category",
      (match detail.PremiumCategory with
       | Some s -> SqlType.String s
       | None -> SqlType.Null)
      "product_category_code", SqlType.String detail.ProductCategoryCode
      "length_spec_lower", SqlType.Decimal detail.LengthSpecLower
      "thickness_spec_lower", SqlType.Decimal detail.ThicknessSpecLower
      "thickness_spec_upper", SqlType.Decimal detail.ThicknessSpecUpper
      "quality_grade", SqlType.String detail.QualityGrade
      "quantity_count", SqlType.Int32(Count.value detail.Count)
      "quantity_amount", SqlType.Decimal(Quantity.value detail.Quantity)
      "inspection_result_category",
      (match detail.InspectionResultCategory with
       | Some s -> SqlType.String s
       | None -> SqlType.Null) ]

let private statusDates (lot: InventoryLot) : DateOnly option * DateOnly option * DateOnly option * string option =
    match lot with
    | Manufacturing _ -> None, None, None, None
    | Manufactured l -> Some l.ManufacturingCompletedDate, None, None, None
    | ConversionInstructed l -> Some l.ManufacturingCompletedDate, None, None, Some l.DestinationInfo.DestinationItem
    | ShippingInstructed l -> Some l.ManufacturingCompletedDate, Some l.ShippingDeadlineDate, None, None
    | Shipped l -> Some l.ManufacturingCompletedDate, Some l.ShippingDeadlineDate, Some l.ShippedDate, None

let private destinationParam (s: string option) : SqlType =
    match s with
    | Some v -> SqlType.String v
    | None -> SqlType.Null

let private lotHeaderParams (common: LotCommon) (status: string) (userId: string) (lot: InventoryLot) : RawDbParams =
    let mfgDate, shipDeadline, shippedDate, destination = statusDates lot
    let (DivisionCode division) = common.DivisionCode
    let (DepartmentCode department) = common.DepartmentCode
    let (SectionCode section) = common.SectionCode

    [ "year", SqlType.Int32 common.LotNumber.Year
      "location", SqlType.String common.LotNumber.Location
      "seq", SqlType.Int32 common.LotNumber.Seq
      "division", SqlType.Int32 division
      "department", SqlType.Int32 department
      "section", SqlType.Int32 section
      "process", SqlType.Int32 common.ProcessCategory
      "inspection", SqlType.Int32 common.InspectionCategory
      "manufacturing", SqlType.Int32 common.ManufacturingCategory
      "status", SqlType.String status
      "mfg_date", dateParam mfgDate
      "ship_deadline", dateParam shipDeadline
      "shipped_date", dateParam shippedDate
      "destination_item", destinationParam destination
      "user_id", SqlType.String userId ]

let private insertLotHeaderSql =
    """
    INSERT INTO lot (
        lot_number_year, lot_number_location, lot_number_seq,
        division_code, department_code, section_code,
        process_category, inspection_category, manufacturing_category,
        status,
        manufacturing_completed_date, shipping_deadline_date, shipped_date,
        destination_item,
        created_by, updated_by
    ) VALUES (
        @year, @location, @seq,
        @division, @department, @section,
        @process, @inspection, @manufacturing,
        @status,
        @mfg_date::date, @ship_deadline::date, @shipped_date::date,
        @destination_item,
        @user_id, @user_id
    )
    """

let private insertLotDetailSql =
    """
    INSERT INTO lot_detail (
        lot_number_year, lot_number_location, lot_number_seq, seq_no,
        item_category, premium_category, product_category_code,
        length_spec_lower, thickness_spec_lower, thickness_spec_upper,
        quality_grade, quantity_count, quantity_amount, inspection_result_category
    ) VALUES (
        @year, @location, @seq, @seq_no,
        @item_category, @premium_category, @product_category_code,
        @length_spec_lower, @thickness_spec_lower, @thickness_spec_upper,
        @quality_grade, @quantity_count, @quantity_amount, @inspection_result_category
    )
    """

let private insertHeader (tran: IDbTransaction) (userId: string) (lot: InventoryLot) : unit =
    let common = InventoryLot.common lot
    let status = InventoryLot.statusString lot

    tran
    |> Db.newCommandForTransaction insertLotHeaderSql
    |> Db.setParams (lotHeaderParams common status userId lot)
    |> Db.exec

let private insertDetail (tran: IDbTransaction) (lotNumber: LotNumber) (seqNo: int) (detail: LotDetail) : unit =
    tran
    |> Db.newCommandForTransaction insertLotDetailSql
    |> Db.setParams (detailParams lotNumber seqNo detail)
    |> Db.exec

let insert (conn: NpgsqlConnection) (userId: string) (lot: InventoryLot) : unit =
    let common = InventoryLot.common lot

    conn
    |> Db.batch (fun tran ->
        insertHeader tran userId lot

        common.Details
        |> NonEmptyList.toList
        |> List.iteri (fun i detail -> insertDetail tran common.LotNumber (i + 1) detail))

let private updateLotSql =
    """
    UPDATE lot
       SET status = @status,
           manufacturing_completed_date = @mfg_date::date,
           shipping_deadline_date = @ship_deadline::date,
           shipped_date = @shipped_date::date,
           destination_item = @destination_item,
           updated_at = NOW(),
           updated_by = @user_id
     WHERE lot_number_year = @year
       AND lot_number_location = @location
       AND lot_number_seq = @seq
    """

let updateStatus (conn: NpgsqlConnection) (userId: string) (lot: InventoryLot) : unit =
    let common = InventoryLot.common lot
    let status = InventoryLot.statusString lot
    let mfgDate, shipDeadline, shippedDate, destination = statusDates lot

    conn
    |> Db.newCommand updateLotSql
    |> Db.setParams
        [ "status", SqlType.String status
          "mfg_date", dateParam mfgDate
          "ship_deadline", dateParam shipDeadline
          "shipped_date", dateParam shippedDate
          "destination_item", destinationParam destination
          "user_id", SqlType.String userId
          "year", SqlType.Int32 common.LotNumber.Year
          "location", SqlType.String common.LotNumber.Location
          "seq", SqlType.Int32 common.LotNumber.Seq ]
    |> Db.exec

let private readDateOnlyOrNull (rd: IDataReader) (col: string) : DateOnly option =
    let idx = rd.GetOrdinal col

    if rd.IsDBNull idx then
        None
    else
        Some(DateOnly.FromDateTime(rd.GetDateTime idx))

let private readStringOrNull (rd: IDataReader) (col: string) : string option =
    let idx = rd.GetOrdinal col
    if rd.IsDBNull idx then None else Some(rd.GetString idx)

let private mapLotDetailRow (rd: IDataReader) : Result<LotDetail, string> =
    let categoryStr = rd.GetString(rd.GetOrdinal "item_category")

    match ItemCategory.tryParse categoryStr with
    | None -> Error(sprintf "Unknown item_category: %s" categoryStr)
    | Some category ->
        let countResult = Count.tryCreate (rd.GetInt32(rd.GetOrdinal "quantity_count"))

        let quantityResult =
            Quantity.tryCreate (rd.GetDecimal(rd.GetOrdinal "quantity_amount"))

        match countResult, quantityResult with
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok count, Ok quantity ->
            Ok
                { ItemCategory = category
                  PremiumCategory = readStringOrNull rd "premium_category"
                  ProductCategoryCode = rd.GetString(rd.GetOrdinal "product_category_code")
                  LengthSpecLower = rd.GetDecimal(rd.GetOrdinal "length_spec_lower")
                  ThicknessSpecLower = rd.GetDecimal(rd.GetOrdinal "thickness_spec_lower")
                  ThicknessSpecUpper = rd.GetDecimal(rd.GetOrdinal "thickness_spec_upper")
                  QualityGrade = rd.GetString(rd.GetOrdinal "quality_grade")
                  Count = count
                  Quantity = quantity
                  InspectionResultCategory = readStringOrNull rd "inspection_result_category" }

type private LotHeader =
    { LotNumber: LotNumber
      DivisionCode: int
      DepartmentCode: int
      SectionCode: int
      ProcessCategory: int
      InspectionCategory: int
      ManufacturingCategory: int
      Status: string
      ManufacturingCompletedDate: DateOnly option
      ShippingDeadlineDate: DateOnly option
      ShippedDate: DateOnly option
      DestinationItem: string option
      Version: int }

let private mapLotHeader (rd: IDataReader) : LotHeader =
    { LotNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
          Location = rd.GetString(rd.GetOrdinal "lot_number_location")
          Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
      DepartmentCode = rd.GetInt32(rd.GetOrdinal "department_code")
      SectionCode = rd.GetInt32(rd.GetOrdinal "section_code")
      ProcessCategory = rd.GetInt32(rd.GetOrdinal "process_category")
      InspectionCategory = rd.GetInt32(rd.GetOrdinal "inspection_category")
      ManufacturingCategory = rd.GetInt32(rd.GetOrdinal "manufacturing_category")
      Status = rd.GetString(rd.GetOrdinal "status")
      ManufacturingCompletedDate = readDateOnlyOrNull rd "manufacturing_completed_date"
      ShippingDeadlineDate = readDateOnlyOrNull rd "shipping_deadline_date"
      ShippedDate = readDateOnlyOrNull rd "shipped_date"
      DestinationItem = readStringOrNull rd "destination_item"
      Version = rd.GetInt32(rd.GetOrdinal "version") }

let private buildInventoryLot (header: LotHeader) (details: NonEmptyList<LotDetail>) : Result<InventoryLot, string> =
    let common: LotCommon =
        { LotNumber = header.LotNumber
          DivisionCode = DivisionCode header.DivisionCode
          DepartmentCode = DepartmentCode header.DepartmentCode
          SectionCode = SectionCode header.SectionCode
          ProcessCategory = header.ProcessCategory
          InspectionCategory = header.InspectionCategory
          ManufacturingCategory = header.ManufacturingCategory
          Details = details }

    match
        header.Status,
        header.ManufacturingCompletedDate,
        header.ShippingDeadlineDate,
        header.ShippedDate,
        header.DestinationItem
    with
    | "manufacturing", _, _, _, _ -> Ok(Manufacturing { Common = common })
    | "manufactured", Some mfg, _, _, _ ->
        Ok(
            Manufactured
                { Common = common
                  ManufacturingCompletedDate = mfg }
        )
    | "conversion_instructed", Some mfg, _, _, Some dest ->
        Ok(
            ConversionInstructed
                { Common = common
                  ManufacturingCompletedDate = mfg
                  DestinationInfo = { DestinationItem = dest } }
        )
    | "shipping_instructed", Some mfg, Some deadline, _, _ ->
        Ok(
            ShippingInstructed
                { Common = common
                  ManufacturingCompletedDate = mfg
                  ShippingDeadlineDate = deadline }
        )
    | "shipped", Some mfg, Some deadline, Some shipped, _ ->
        Ok(
            Shipped
                { Common = common
                  ManufacturingCompletedDate = mfg
                  ShippingDeadlineDate = deadline
                  ShippedDate = shipped }
        )
    | other, _, _, _, _ -> Error(sprintf "Inconsistent lot row: status=%s" other)

let private lotKeyParams (lotNumber: LotNumber) : RawDbParams =
    [ "year", SqlType.Int32 lotNumber.Year
      "location", SqlType.String lotNumber.Location
      "seq", SqlType.Int32 lotNumber.Seq ]

let private fetchHeader (conn: NpgsqlConnection) (lotNumber: LotNumber) : LotHeader option =
    conn
    |> Db.newCommand
        """
        SELECT lot_number_year, lot_number_location, lot_number_seq,
               division_code, department_code, section_code,
               process_category, inspection_category, manufacturing_category,
               status,
               manufacturing_completed_date, shipping_deadline_date, shipped_date,
               destination_item,
               version
          FROM lot
         WHERE lot_number_year = @year
           AND lot_number_location = @location
           AND lot_number_seq = @seq
        """
    |> Db.setParams (lotKeyParams lotNumber)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.querySingle mapLotHeader

let private fetchDetailRows (conn: NpgsqlConnection) (lotNumber: LotNumber) : Result<LotDetail, string> list =
    conn
    |> Db.newCommand
        """
        SELECT item_category, premium_category, product_category_code,
               length_spec_lower, thickness_spec_lower, thickness_spec_upper,
               quality_grade, quantity_count, quantity_amount, inspection_result_category
          FROM lot_detail
         WHERE lot_number_year = @year
           AND lot_number_location = @location
           AND lot_number_seq = @seq
         ORDER BY seq_no
        """
    |> Db.setParams (lotKeyParams lotNumber)
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.query mapLotDetailRow

let private collapseDetailResults (results: Result<LotDetail, string> list) : Result<LotDetail list, string> =
    let step (acc: Result<LotDetail list, string>) (r: Result<LotDetail, string>) =
        match acc, r with
        | Error e, _ -> Error e
        | Ok _, Error e -> Error e
        | Ok xs, Ok d -> Ok(d :: xs)

    results |> List.fold step (Ok []) |> Result.map List.rev

let private buildLoaded
    (conn: NpgsqlConnection)
    (lotNumber: LotNumber)
    (header: LotHeader)
    : Result<(InventoryLot * int) option, string> =
    match collapseDetailResults (fetchDetailRows conn lotNumber) with
    | Error e -> Error e
    | Ok [] -> Error(sprintf "Lot %s has no details" (LotNumber.toString lotNumber))
    | Ok ordered ->
        match NonEmptyList.ofList ordered with
        | None -> Error "details became empty unexpectedly"
        | Some nel ->
            buildInventoryLot header nel
            |> Result.map (fun lot -> Some(lot, header.Version))

let loadWithVersion (conn: NpgsqlConnection) (lotNumber: LotNumber) : Result<(InventoryLot * int) option, string> =
    match fetchHeader conn lotNumber with
    | None -> Ok None
    | Some header -> buildLoaded conn lotNumber header

let load (conn: NpgsqlConnection) (lotNumber: LotNumber) : Result<InventoryLot option, string> =
    loadWithVersion conn lotNumber |> Result.map (Option.map fst)

let private updateWithVersionSql =
    """
    UPDATE lot
       SET status = @status,
           manufacturing_completed_date = @mfg_date::date,
           shipping_deadline_date = @ship_deadline::date,
           shipped_date = @shipped_date::date,
           destination_item = @destination_item,
           updated_at = NOW(),
           updated_by = @user_id,
           version = version + 1
     WHERE lot_number_year = @year
       AND lot_number_location = @location
       AND lot_number_seq = @seq
       AND version = @expected_version
    RETURNING version
    """

let updateWithVersion
    (conn: NpgsqlConnection)
    (userId: string)
    (lot: InventoryLot)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    let common = InventoryLot.common lot
    let status = InventoryLot.statusString lot
    let mfgDate, shipDeadline, shippedDate, destination = statusDates lot

    let newVersion =
        conn
        |> Db.newCommand updateWithVersionSql
        |> Db.setParams
            [ "status", SqlType.String status
              "mfg_date", dateParam mfgDate
              "ship_deadline", dateParam shipDeadline
              "shipped_date", dateParam shippedDate
              "destination_item", destinationParam destination
              "user_id", SqlType.String userId
              "year", SqlType.Int32 common.LotNumber.Year
              "location", SqlType.String common.LotNumber.Location
              "seq", SqlType.Int32 common.LotNumber.Seq
              "expected_version", SqlType.Int32 expectedVersion ]
        |> Db.scalar (fun (v: obj) ->
            if isNull v || (v :? System.DBNull) then
                0
            else
                Convert.ToInt32 v)

    if newVersion <= 0 then
        Error(SalesManagement.Domain.Errors.OptimisticLockConflict("Lot", LotNumber.toString common.LotNumber))
    else
        Ok newVersion

type LotCsvRow =
    { LotNumber: LotNumber
      DivisionCode: int
      Status: string
      ManufacturingCompletedDate: DateOnly option }

let private mapLotCsvRow (rd: IDataReader) : LotCsvRow =
    { LotNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
          Location = rd.GetString(rd.GetOrdinal "lot_number_location")
          Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code")
      Status = rd.GetString(rd.GetOrdinal "status")
      ManufacturingCompletedDate = readDateOnlyOrNull rd "manufacturing_completed_date" }

let listForCsv (conn: NpgsqlConnection) (statusFilter: string option) : LotCsvRow list =
    let baseSelect =
        """
        SELECT lot_number_year, lot_number_location, lot_number_seq,
               division_code, status, manufacturing_completed_date
          FROM lot
        """

    let orderBy = " ORDER BY lot_number_year, lot_number_location, lot_number_seq"

    match statusFilter with
    | None ->
        conn
        |> Db.newCommand (baseSelect + orderBy)
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query mapLotCsvRow
    | Some s ->
        conn
        |> Db.newCommand (baseSelect + " WHERE status = @status" + orderBy)
        |> Db.setParams [ "status", SqlType.String s ]
        |> Db.setCommandBehavior CommandBehavior.Default
        |> Db.query mapLotCsvRow

let updateWithVersionTx
    (tx: NpgsqlTransaction)
    (userId: string)
    (lot: InventoryLot)
    (expectedVersion: int)
    : Result<int, SalesManagement.Domain.Errors.DomainError> =
    let common = InventoryLot.common lot
    let status = InventoryLot.statusString lot
    let mfgDate, shipDeadline, shippedDate, destination = statusDates lot

    use cmd = tx.Connection.CreateCommand()
    cmd.Transaction <- tx
    cmd.CommandText <- updateWithVersionSql

    let addParam (name: string) (value: obj) =
        let p = cmd.CreateParameter()
        p.ParameterName <- name
        p.Value <- (if isNull value then box DBNull.Value else value)
        cmd.Parameters.Add p |> ignore

    let dateValue (d: DateOnly option) : obj =
        match d with
        | Some date -> box (date.ToString("yyyy-MM-dd"))
        | None -> box DBNull.Value

    let stringOption (s: string option) : obj =
        match s with
        | Some v -> box v
        | None -> box DBNull.Value

    addParam "status" (box status)
    addParam "mfg_date" (dateValue mfgDate)
    addParam "ship_deadline" (dateValue shipDeadline)
    addParam "shipped_date" (dateValue shippedDate)
    addParam "destination_item" (stringOption destination)
    addParam "user_id" (box userId)
    addParam "year" (box common.LotNumber.Year)
    addParam "location" (box common.LotNumber.Location)
    addParam "seq" (box common.LotNumber.Seq)
    addParam "expected_version" (box expectedVersion)

    let result = cmd.ExecuteScalar()

    let newVersion =
        if isNull result || (result :? DBNull) then
            0
        else
            Convert.ToInt32 result

    if newVersion <= 0 then
        Error(SalesManagement.Domain.Errors.OptimisticLockConflict("Lot", LotNumber.toString common.LotNumber))
    else
        Ok newVersion
