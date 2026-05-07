// ============================================================
// リファレンス実装: 在庫ロット ドメイン
// ============================================================
// このファイルは「DSL→F#コードの正しい翻訳」のゴールド標準である。
// AIによるコード生成・人間の実装は、このスタイルに準拠すること。
//
// 出典: dsl/domain-model.md（在庫ロット部分）
//
// このリファレンスがカバーする範囲:
//   - 値型の smart constructor（DSL の where 制約の表現）
//   - 状態の場合分け（DSL の OR を判別共用体に）
//   - 振る舞いの実装（DSL の behavior を純粋関数に）
//   - エラー型（OR エラー を判別共用体に）
//   - railway-oriented programming による合成
//
// その他のドメイン（販売案件・価格査定・販売契約）も同じパターンを適用すること。
// ============================================================

namespace SalesManagement.Reference

open System

// ============================================================
// 1. 共通ユーティリティ
// ============================================================

/// 空でないリスト
/// DSL: List<X> // 1件以上 → NonEmptyList<X>
type NonEmptyList<'T> = private NonEmptyList of head: 'T * tail: 'T list

module NonEmptyList =
    let create (head: 'T) (tail: 'T list) : NonEmptyList<'T> =
        NonEmptyList(head, tail)

    let tryCreate (items: 'T list) : Result<NonEmptyList<'T>, string> =
        match items with
        | [] -> Error "リストは1件以上必要"
        | h :: t -> Ok (NonEmptyList(h, t))

    let toList (NonEmptyList(h, t)) = h :: t

    let head (NonEmptyList(h, _)) = h

// ============================================================
// 2. 値型（Value Types）
// ------------------------------------------------------------
// 設計原則: Make Illegal States Unrepresentable
// where 制約のある値型は private constructor + smart constructor で実現する。
// これにより、不正な値を持つインスタンスを型レベルで作れなくする。
// ============================================================

/// 金額（DSL: data 金額 = 整数 // 0以上、円単位）
type Amount = private Amount of int64

module Amount =
    /// 0以上の整数のみ受け付ける smart constructor
    let create (value: int64) : Result<Amount, string> =
        if value >= 0L then Ok (Amount value)
        else Error $"金額は0以上である必要があります (入力: {value})"

    let value (Amount v) = v

/// 数量（DSL: data 数量 = 数値 // 0.001以上）
type Quantity = private Quantity of decimal

module Quantity =
    let create (value: decimal) : Result<Quantity, string> =
        if value >= 0.001m then Ok (Quantity value)
        else Error $"数量は0.001以上である必要があります (入力: {value})"

    let value (Quantity v) = v

/// 個数（DSL: data 個数 = 整数 // 1以上）
type Count = private Count of int

module Count =
    let create (value: int) : Result<Count, string> =
        if value >= 1 then Ok (Count value)
        else Error $"個数は1以上である必要があります (入力: {value})"

    let value (Count v) = v

// ------------------------------------------------------------
// 識別子型（DSL の単純な値型は branded type で衝突を防ぐ）
// ------------------------------------------------------------

type DivisionCode = DivisionCode of int
type DepartmentCode = DepartmentCode of int
type SectionCode = SectionCode of int

// ============================================================
// 3. ロット番号（複合キー）
// DSL: data ロット番号 = ロット番号年度 AND ロット番号保管場所 AND ロット番号連番
// AND は record として表現する。
// ============================================================

type LotNumberYear = LotNumberYear of int
type LotNumberStorageLocation = LotNumberStorageLocation of string
type LotNumberSequence = LotNumberSequence of int

type LotNumber = {
    Year: LotNumberYear
    StorageLocation: LotNumberStorageLocation
    Sequence: LotNumberSequence
}

// ============================================================
// 4. 区分の列挙
// ------------------------------------------------------------
// DSL では「整数」と表現されている区分も、判別可能な選択肢が決まっているなら
// 判別共用体に厳密化する（暗黙の整数より型安全）
// ============================================================

/// 工程区分（DSL: data 工程区分 = 整数）
/// 注: DSL は整数だが、業務上は有限の選択肢なので列挙化する
type ProcessClassification =
    | InspectionProcess
    | ManufacturingProcess
    | ShippingProcess

/// 検査区分
type InspectionClassification =
    | Inspected
    | NotInspected

/// 製造区分
type ManufacturingClassification =
    | Standard
    | Custom

/// 品目区分（DSL: data 品目区分 = 一般品 OR 上位品 OR 特注品）
/// OR は判別共用体に1対1で対応
type ItemClassification =
    | Standard       // 一般品
    | Premium        // 上位品
    | CustomMade     // 特注品

// ============================================================
// 5. ロット明細
// DSL: data ロット明細 = 品目区分 AND 上位品区分? AND ...
// 「?」は Option/nullable に対応
// ============================================================

type GradeClassification = GradeClassification of string
type LengthLowerSpec = LengthLowerSpec of decimal
type ThicknessLowerSpec = ThicknessLowerSpec of decimal
type ThicknessUpperSpec = ThicknessUpperSpec of decimal
type ProductClassificationCode = ProductClassificationCode of string
type PremiumClassification = PremiumClassification of string
type InspectionResultClassification = InspectionResultClassification of string

type LotItem = {
    ItemClassification: ItemClassification
    PremiumClassification: PremiumClassification option   // DSL: 上位品区分?
    ProductClassificationCode: ProductClassificationCode
    LengthLowerSpec: LengthLowerSpec
    ThicknessLowerSpec: ThicknessLowerSpec
    ThicknessUpperSpec: ThicknessUpperSpec
    GradeClassification: GradeClassification
    Count: Count
    Quantity: Quantity
    InspectionResult: InspectionResultClassification option // DSL: 検査結果区分?
}

// ============================================================
// 6. ロット共通
// DSL: data ロット共通 = ロット番号 AND ... AND List<ロット明細> // 1件以上
// すべての在庫ロット状態に共通する部分。
// ============================================================

type LotCommon = {
    LotNumber: LotNumber
    DivisionCode: DivisionCode
    DepartmentCode: DepartmentCode
    SectionCode: SectionCode
    ProcessClassification: ProcessClassification
    InspectionClassification: InspectionClassification
    ManufacturingClassification: ManufacturingClassification
    LotItems: NonEmptyList<LotItem>   // DSL: List<ロット明細> // 1件以上
}

// ============================================================
// 7. 在庫ロットの状態（直和型による状態の場合分け）
// ------------------------------------------------------------
// DSL: data 在庫ロット = 製造中ロット OR 製造完了ロット OR ...
// 設計原則: 状態ごとに必要なフィールドが異なるため、フラグではなく型で表現する。
// これにより「製造完了日のない出荷完了ロット」のような不正な状態が
// コンパイル時に作れなくなる。
// ============================================================

/// 製造中ロット（DSL: data 製造中ロット = ロット共通）
type ManufacturingLot = {
    Common: LotCommon
}

/// 製造完了ロット（DSL: data 製造完了ロット = ロット共通 AND 製造完了日）
type ManufacturedLot = {
    Common: LotCommon
    ManufacturingCompletedDate: DateOnly
}

/// 出荷指示済みロット（DSL: data 出荷指示済みロット = ロット共通 AND 製造完了日 AND 出荷期限日）
type ShippingInstructedLot = {
    Common: LotCommon
    ManufacturingCompletedDate: DateOnly
    ShippingDeadlineDate: DateOnly
}

/// 出荷完了ロット（DSL: data 出荷完了ロット = ロット共通 AND 製造完了日 AND 出荷期限日 AND 出荷日）
type ShippedLot = {
    Common: LotCommon
    ManufacturingCompletedDate: DateOnly
    ShippingDeadlineDate: DateOnly
    ShippingDate: DateOnly
}

/// 変換先情報
type ConversionTarget = {
    TargetClassification: ItemClassification
    Reason: string
}

/// 変換指示済みロット（DSL: data 変換指示済みロット = ロット共通 AND 製造完了日 AND 変換先情報）
type ConversionInstructedLot = {
    Common: LotCommon
    ManufacturingCompletedDate: DateOnly
    ConversionTarget: ConversionTarget
}

/// 在庫ロット（DSL の最上位の直和型）
type InventoryLot =
    | Manufacturing of ManufacturingLot
    | Manufactured of ManufacturedLot
    | ShippingInstructed of ShippingInstructedLot
    | Shipped of ShippedLot
    | ConversionInstructed of ConversionInstructedLot

// ============================================================
// 8. エラー型（Errors as Values）
// ------------------------------------------------------------
// 設計原則: 例外を投げず、Result<T, Error> で返す。
// エラー型は判別共用体で構造化する（文字列メッセージは避ける）。
// これにより呼び出し側で網羅的なエラーハンドリングを強制できる。
//
// 注: DSL は「OR エラー」と書くだけでエラーバリアントの構造は未定義。
// 実装時にこのファイルで定義し、DSLにフィードバックすること。
// ============================================================

type ManufacturingCompletionError =
    /// 製造完了日が業務的に不正
    | InvalidManufacturingCompletedDate of attemptedDate: DateOnly * reason: string

type ShippingInstructionError =
    /// 出荷期限日が製造完了日より前
    | InvalidShippingDeadlineDate of deadline: DateOnly * manufacturingCompletedDate: DateOnly

type ShippingCompletionError =
    /// 出荷日が出荷期限日より後
    | InvalidShippingDate of shippingDate: DateOnly * deadline: DateOnly

type ManufacturingCompletionCancellationError =
    /// 取消不可（業務ルール上）
    | CannotCancel of reason: string

type ConversionInstructionError =
    /// 変換先が不正
    | InvalidConversionTarget of reason: string

type ConversionInstructionCancellationError =
    /// 取消不可
    | NotInConversionState

// ============================================================
// 9. 振る舞い（Workflows as Functions）
// ------------------------------------------------------------
// DSL: behavior X = Input -> Output OR Error
//
// 設計原則:
//   1. 全域関数（部分関数を避ける）
//   2. 副作用なし（純粋関数）。永続化は別レイヤー（Infrastructure）で行う
//   3. 状態遷移は型レベルで強制
//      （例：ManufacturingLot を受ける関数は ManufacturedLot から呼べない）
//   4. エラーは Result 型で返す
//   5. 共通フィールドは透過的に伝搬する
// ============================================================

/// 製造完了を指示する
/// DSL: behavior 製造完了を指示する = 製造中ロット AND 製造完了日 -> 製造完了ロット OR 製造完了指示エラー
///
/// 注: DSL に事前条件の明記が無いため、現状は受理する実装。
/// 業務ルール（例：「未来日不可」「製造開始日との比較」など）が決まったら
/// ここに追加し、DSL も更新すること。
let completeManufacturing
    (lot: ManufacturingLot)
    (manufacturingCompletedDate: DateOnly)
    : Result<ManufacturedLot, ManufacturingCompletionError> =
    Ok {
        Common = lot.Common
        ManufacturingCompletedDate = manufacturingCompletedDate
    }

/// 出荷を指示する
/// DSL: behavior 出荷を指示する = 製造完了ロット AND 出荷期限日 -> 出荷指示済みロット OR 出荷指示エラー
let instructShipping
    (lot: ManufacturedLot)
    (deadline: DateOnly)
    : Result<ShippingInstructedLot, ShippingInstructionError> =
    if deadline < lot.ManufacturingCompletedDate then
        Error (InvalidShippingDeadlineDate(deadline, lot.ManufacturingCompletedDate))
    else
        Ok {
            Common = lot.Common
            ManufacturingCompletedDate = lot.ManufacturingCompletedDate
            ShippingDeadlineDate = deadline
        }

/// 出荷完了を指示する
/// DSL: behavior 出荷完了を指示する = 出荷指示済みロット AND 出荷日 -> 出荷完了ロット OR 出荷完了指示エラー
let completeShipping
    (lot: ShippingInstructedLot)
    (shippingDate: DateOnly)
    : Result<ShippedLot, ShippingCompletionError> =
    if shippingDate > lot.ShippingDeadlineDate then
        Error (InvalidShippingDate(shippingDate, lot.ShippingDeadlineDate))
    else
        Ok {
            Common = lot.Common
            ManufacturingCompletedDate = lot.ManufacturingCompletedDate
            ShippingDeadlineDate = lot.ShippingDeadlineDate
            ShippingDate = shippingDate
        }

/// 製造完了を取り消す
/// DSL: behavior 製造完了を取り消す = 製造完了ロット -> 製造中ロット OR 取消エラー
let cancelManufacturingCompletion
    (lot: ManufacturedLot)
    : Result<ManufacturingLot, ManufacturingCompletionCancellationError> =
    Ok {
        Common = lot.Common
    }

/// 品目変換を指示する
/// DSL: behavior 品目変換を指示する = 製造完了ロット AND 変換先情報 -> 変換指示済みロット OR 変換指示エラー
let instructConversion
    (lot: ManufacturedLot)
    (target: ConversionTarget)
    : Result<ConversionInstructedLot, ConversionInstructionError> =
    Ok {
        Common = lot.Common
        ManufacturingCompletedDate = lot.ManufacturingCompletedDate
        ConversionTarget = target
    }

/// 品目変換指示を取り消す
/// DSL: behavior 品目変換指示を取り消す = 変換指示済みロット -> 製造完了ロット OR 変換指示取消エラー
let cancelConversionInstruction
    (lot: ConversionInstructedLot)
    : Result<ManufacturedLot, ConversionInstructionCancellationError> =
    Ok {
        Common = lot.Common
        ManufacturingCompletedDate = lot.ManufacturingCompletedDate
    }

// ============================================================
// 10. ワークフローの合成例（Railway-Oriented Programming）
// ------------------------------------------------------------
// 小さい関数を Result.bind で繋いで複雑なワークフローを構築する。
// エラーは自動で短絡（short-circuit）し、後続の処理はスキップされる。
// ============================================================

/// 統合エラー型（複数のエラーを束ねる）
type LotWorkflowError =
    | CompletionFailed of ManufacturingCompletionError
    | ShippingFailed of ShippingInstructionError

/// 製造完了→出荷指示までを一括で実行する例
let manufactureAndInstructShipping
    (lot: ManufacturingLot)
    (manufacturingCompletedDate: DateOnly)
    (shippingDeadlineDate: DateOnly)
    : Result<ShippingInstructedLot, LotWorkflowError> =
    completeManufacturing lot manufacturingCompletedDate
    |> Result.mapError CompletionFailed
    |> Result.bind (fun completed ->
        instructShipping completed shippingDeadlineDate
        |> Result.mapError ShippingFailed)

// ============================================================
// 11. アクティブパターン（オプション）
// ------------------------------------------------------------
// InventoryLot 全体を扱う関数で、状態に応じた処理を書きやすくする
// ============================================================

/// 在庫ロットから共通部分を取り出す
let getCommon (lot: InventoryLot) : LotCommon =
    match lot with
    | Manufacturing m -> m.Common
    | Manufactured m -> m.Common
    | ShippingInstructed s -> s.Common
    | Shipped sh -> sh.Common
    | ConversionInstructed ci -> ci.Common

/// 在庫ロットが出荷可能な状態か
let isShippingReady (lot: InventoryLot) : bool =
    match lot with
    | Manufactured _ -> true
    | _ -> false
