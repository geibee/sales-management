module SalesManagement.Infrastructure.SqlParams

open System
open Donald

/// Option 値を Donald の SqlType へ落とす共通ヘルパ。
/// LotRepository / SalesCaseRepository / AppraisalRepository で同一実装が
/// 重複していたため集約した (FSharpLint マージゲート化に伴う整理)。

let dateParam (d: DateOnly option) : SqlType =
    match d with
    | Some date -> SqlType.AnsiString(date.ToString("yyyy-MM-dd"))
    | None -> SqlType.Null

let optionString (s: string option) : SqlType =
    match s with
    | Some v -> SqlType.String v
    | None -> SqlType.Null

let optionInt (i: int option) : SqlType =
    match i with
    | Some v -> SqlType.Int32 v
    | None -> SqlType.Null

let optionDecimal (d: decimal option) : SqlType =
    match d with
    | Some v -> SqlType.Decimal v
    | None -> SqlType.Null
