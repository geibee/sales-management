module SalesManagement.Tests.Support.RequestBuilders

open System.Globalization
open System.Text

/// JSON 値の差分ビルド用 DU。`null` を「欠落 (`Option.None`)」と区別できる点が要点で、
/// S2 マトリクスで「欠落 / null / 不正値 / 余剰」を 4 通り別々に検証するための基盤。
type JsonValue =
    | JString of string
    | JInt of int
    | JLong of int64
    | JDecimal of decimal
    | JFloat of float
    | JBool of bool
    | JNull
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list
    /// 既存 JSON 文字列をそのまま埋め込む（テスト fixture から大きな塊をコピペするとき用）
    | JRaw of string

let private escapeString (s: string) : string =
    let sb = StringBuilder()
    sb.Append '"' |> ignore

    for c in s do
        match c with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | c when int c < 0x20 -> sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", int c) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

let rec render (v: JsonValue) : string =
    match v with
    | JString s -> escapeString s
    | JInt i -> string i
    | JLong l -> string l
    | JDecimal d -> d.ToString(CultureInfo.InvariantCulture)
    | JFloat f -> f.ToString("R", CultureInfo.InvariantCulture)
    | JBool true -> "true"
    | JBool false -> "false"
    | JNull -> "null"
    | JArray items -> "[" + (items |> List.map render |> String.concat ",") + "]"
    | JObject fields ->
        let body =
            fields
            |> List.map (fun (k, v) -> escapeString k + ":" + render v)
            |> String.concat ","

        "{" + body + "}"
    | JRaw raw -> raw

/// 単一フィールドの上書き仕様。
/// `None` = デフォルト値を使う、`Some v` = 値を `v` で置換（`Some JNull` で明示的 null）。
type FieldOverride = JsonValue option

let private applyOverrides
    (defaults: (string * JsonValue) list)
    (overrides: (string * FieldOverride) list)
    (extra: (string * JsonValue) list)
    (omit: string Set)
    : (string * JsonValue) list =
    let overrideMap = overrides |> List.map (fun (k, v) -> k, v) |> Map.ofList

    let baseFields =
        defaults
        |> List.choose (fun (k, dflt) ->
            if omit.Contains k then
                None
            else
                match overrideMap |> Map.tryFind k with
                | Some(Some v) -> Some(k, v)
                | Some None -> Some(k, dflt)
                | None -> Some(k, dflt))

    baseFields @ extra

/// `POST /lots` ボディの差分指定。
/// すべて `None` でデフォルト正常値（小ロット 1 件）を生成する。
type LotBodyOverrides =
    {
        Year: FieldOverride
        Location: FieldOverride
        Seq: FieldOverride
        DivisionCode: FieldOverride
        DepartmentCode: FieldOverride
        SectionCode: FieldOverride
        ProcessCategory: FieldOverride
        InspectionCategory: FieldOverride
        ManufacturingCategory: FieldOverride
        Details: FieldOverride
        /// 余剰フィールド注入（OpenAPI に無いキー）
        Extra: (string * JsonValue) list
        /// 完全に削除するトップレベルフィールド名
        Omit: string list
    }

let emptyLotOverrides: LotBodyOverrides =
    { Year = None
      Location = None
      Seq = None
      DivisionCode = None
      DepartmentCode = None
      SectionCode = None
      ProcessCategory = None
      InspectionCategory = None
      ManufacturingCategory = None
      Details = None
      Extra = []
      Omit = [] }

let private defaultLotDetailFields: (string * JsonValue) list =
    [ "itemCategory", JString "premium"
      "premiumCategory", JString "A"
      "productCategoryCode", JString "v1"
      "lengthSpecLower", JFloat 1.0
      "thicknessSpecLower", JFloat 1.0
      "thicknessSpecUpper", JFloat 2.0
      "qualityGrade", JString "A"
      "count", JInt 1
      "quantity", JFloat 10.0
      "inspectionResultCategory", JString "pass" ]

/// 単一 LotDetail の正常値 baseline を、指定キーだけ差分上書きして返す。
/// S2 系で `details[]` 内の値を境界テストするときに、テスト側で baseline を再定義しないで済むようにする共通ヘルパー。
let lotDetailWith (overrides: (string * JsonValue) list) : JsonValue =
    let m = Map.ofList overrides

    JObject(
        defaultLotDetailFields
        |> List.map (fun (k, v) -> k, m |> Map.tryFind k |> Option.defaultValue v)
    )

let private defaultLotDetail: JsonValue = JObject defaultLotDetailFields

let private defaultLotNumber (year: JsonValue) (location: JsonValue) (seq: JsonValue) : JsonValue =
    JObject [ "year", year; "location", location; "seq", seq ]

/// 正常値で `POST /lots` のボディを生成する。Overrides を渡すと該当フィールドを差分上書きする。
let createLotBody (ov: LotBodyOverrides) : string =
    let year = ov.Year |> Option.defaultValue (JInt 2026)
    let location = ov.Location |> Option.defaultValue (JString "F12A")
    let seq = ov.Seq |> Option.defaultValue (JInt 1)

    let lotNumber = defaultLotNumber year location seq

    let defaults =
        [ "lotNumber", lotNumber
          "divisionCode", JInt 1
          "departmentCode", JInt 10
          "sectionCode", JInt 100
          "processCategory", JInt 1
          "inspectionCategory", JInt 1
          "manufacturingCategory", JInt 1
          "details", JArray [ defaultLotDetail ] ]

    let overrides =
        [ "divisionCode", ov.DivisionCode
          "departmentCode", ov.DepartmentCode
          "sectionCode", ov.SectionCode
          "processCategory", ov.ProcessCategory
          "inspectionCategory", ov.InspectionCategory
          "manufacturingCategory", ov.ManufacturingCategory
          "details", ov.Details ]

    let omit = Set.ofList ov.Omit
    let fields = applyOverrides defaults overrides ov.Extra omit
    render (JObject fields)

/// `POST /sales-cases` ボディの差分指定。
type SalesCaseBodyOverrides =
    { Lots: FieldOverride
      DivisionCode: FieldOverride
      SalesDate: FieldOverride
      CaseType: FieldOverride
      Extra: (string * JsonValue) list
      Omit: string list }

let emptySalesCaseOverrides: SalesCaseBodyOverrides =
    { Lots = None
      DivisionCode = None
      SalesDate = None
      CaseType = None
      Extra = []
      Omit = [] }

let createSalesCaseBody (ov: SalesCaseBodyOverrides) : string =
    let lots = ov.Lots |> Option.defaultValue (JArray [])

    let defaults =
        [ "lots", lots
          "divisionCode", JInt 1
          "salesDate", JString "2026-04-15"
          "caseType", JString "direct" ]

    let overrides =
        [ "lots", ov.Lots
          "divisionCode", ov.DivisionCode
          "salesDate", ov.SalesDate
          "caseType", ov.CaseType ]

    let omit = Set.ofList ov.Omit
    let fields = applyOverrides defaults overrides ov.Extra omit
    render (JObject fields)

/// `version` のみを持つボディ（楽観ロック取消系で頻出）
let versionOnlyBody (version: JsonValue) : string = render (JObject [ "version", version ])

/// `version` + 任意の日付フィールドを持つボディ（出庫日等の状態遷移エンドポイント共通形）
let dateVersionBody (dateField: string) (date: JsonValue) (version: JsonValue) : string =
    render (JObject [ dateField, date; "version", version ])

// ───────────────────────────────────────────────────────────────
// 販売案件サブタイプ操作の共通ボディ
// (AppraisalContractParamTests とステートフル PBT で共用する)
// ───────────────────────────────────────────────────────────────

/// defaults を overrides で差分上書きし、omit のキーを取り除いて JSON を返す。
let buildBodyWith
    (defaults: (string * JsonValue) list)
    (overrides: (string * JsonValue) list)
    (omit: string list)
    : string =
    let m = Map.ofList overrides
    let omitSet = Set.ofList omit

    let fields =
        defaults
        |> List.choose (fun (k, dflt) ->
            if omitSet.Contains k then
                None
            else
                m |> Map.tryFind k |> Option.defaultValue dflt |> (fun v -> Some(k, v)))

    render (JObject fields)

/// direct 査定の lotAppraisals 要素 1 件分 (正常値)。
let directLotAppraisal (lotId: string) : JsonValue =
    JObject
        [ "lotNumber", JString lotId
          "detailAppraisals",
          JArray
              [ JObject
                    [ "detailIndex", JInt 1
                      "baseUnitPrice", JInt 1000
                      "periodAdjustmentRate", JDecimal 1m
                      "counterpartyAdjustmentRate", JDecimal 1m ] ] ]

/// `POST/PUT /sales-cases/{id}/appraisals` (direct) の正常ボディ。
let directAppraisalBody
    (lotId: string)
    (version: int)
    (overrides: (string * JsonValue) list)
    (omit: string list)
    : string =
    buildBodyWith
        [ "type", JString "normal"
          "appraisalDate", JString "2026-01-20"
          "deliveryDate", JString "2026-01-25"
          "salesMarket", JString "market"
          "baseUnitPriceDate", JString "2026-01-01"
          "periodAdjustmentRateDate", JString "2026-01-01"
          "counterpartyAdjustmentRateDate", JString "2026-01-01"
          "taxExcludedEstimatedTotal", JInt 100000
          "lotAppraisals", JArray [ directLotAppraisal lotId ]
          "version", JInt version ]
        overrides
        omit

/// `POST /sales-cases/{id}/contracts` (direct) の正常ボディ。
let directContractBody (version: int) (overrides: (string * JsonValue) list) (omit: string list) : string =
    buildBodyWith
        [ "contractDate", JString "2026-02-01"
          "person", JString "person"
          "buyer", JObject [ "customerNumber", JString "CUST001"; "agentName", JString "agent" ]
          "salesType", JInt 1
          "item", JString "item"
          "deliveryMethod", JString "method"
          "paymentDeferralCondition", JString ""
          "salesMethod", JInt 1
          "usage", JString ""
          "taxExcludedContractAmount", JInt 100000
          "consumptionTax", JInt 10000
          "taxExcludedPaymentAmount", JInt 100000
          "paymentConsumptionTax", JInt 10000
          "version", JInt version ]
        overrides
        omit

/// `POST /sales-cases/{id}/reservation/appraisals` の正常ボディ。
let reservationAppraisalBody (version: int) : string =
    render (
        JObject
            [ "appraisalDate", JString "2026-01-20"
              "reservedLotInfo", JString "reserved-lot-info"
              "reservedAmount", JInt 1000
              "version", JInt version ]
    )

/// `POST /sales-cases/{id}/reservation/determine` の正常ボディ。
let reservationDetermineBody (version: int) : string =
    render (
        JObject
            [ "determinedDate", JString "2026-02-01"
              "determinedAmount", JInt 1200
              "version", JInt version ]
    )

/// `POST /sales-cases/{id}/reservation/delivery` の正常ボディ。
let reservationDeliveryBody (version: int) : string =
    render (JObject [ "deliveryDate", JString "2026-03-01"; "version", JInt version ])

/// `POST /sales-cases/{id}/consignment/designate` の正常ボディ。
let consignmentDesignateBody (version: int) : string =
    render (
        JObject
            [ "consignorName", JString "consignor"
              "consignorCode", JString "C001"
              "designatedDate", JString "2026-01-20"
              "version", JInt version ]
    )

/// `POST /sales-cases/{id}/consignment/result` の正常ボディ。
let consignmentResultBody (version: int) : string =
    render (
        JObject
            [ "resultDate", JString "2026-03-01"
              "resultAmount", JInt 900
              "version", JInt version ]
    )
