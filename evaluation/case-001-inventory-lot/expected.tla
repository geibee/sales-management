---------------------------- MODULE InventoryLot ----------------------------
(* ============================================================================
   在庫ロットドメイン TLA+ 仕様 (case-001 expected.tla)
   ============================================================================
   出典: dsl/domain-model.md（在庫ロット部分）
   翻訳規則: harness/SEMANTICS.md の TLA+ 章

   スコープ: [CORE] サブセットのみ。
   - 状態変数: inventoryLots（在庫ロット集合）
   - アクション: 6 個の behavior をそのまま TLA+ アクションに翻訳
   - 型不変条件 TypeOK のみ含める（基本的な safety）
   - 業務的不変条件 (invariant) と liveness (property) は [VERIFICATION]
     なのでこの仕様には含めない（P4 で追加）。
   ============================================================================ *)

EXTENDS Naturals, FiniteSets

(* ----------------------------------------------------------------------------
   定数: 値型の domain
   ----------------------------------------------------------------------------
   実検証では適切な小規模有限集合に置き換える。
   ---------------------------------------------------------------------------- *)
CONSTANTS
    DateOnly,                         (* 日付の抽象集合 *)
    LotNumberYears,                   (* ロット番号年度 *)
    LotNumberStorageLocations,        (* ロット番号保管場所 *)
    LotNumberSequences,               (* ロット番号連番 *)
    DivisionCodes,                    (* 事業部コード *)
    DepartmentCodes,                  (* 部門コード *)
    SectionCodes,                     (* 担当課コード *)
    GradeClassifications,
    LengthLowerSpecs,
    ThicknessLowerSpecs,
    ThicknessUpperSpecs,
    ProductClassificationCodes,
    PremiumClassifications,
    InspectionResultClassifications,
    Reasons                           (* 変換先情報の理由 *)

(* ----------------------------------------------------------------------------
   値型 (where 制約のあるものは集合内包記法で絞る)
   ---------------------------------------------------------------------------- *)
Amount   == { x \in Nat : x >= 0 }    \* 金額: 0以上
Count    == { x \in Nat : x >= 1 }    \* 個数: 1以上
Quantity == Nat                       \* 数量: decimal 域は Nat で抽象化

(* ----------------------------------------------------------------------------
   識別子型
   ---------------------------------------------------------------------------- *)
LotNumber == [
    year             : LotNumberYears,
    storageLocation  : LotNumberStorageLocations,
    sequence         : LotNumberSequences
]

(* ----------------------------------------------------------------------------
   区分（OR を文字列タグで表現）
   ---------------------------------------------------------------------------- *)
ProcessClassification       == {"InspectionProcess", "ManufacturingProcess", "ShippingProcess"}
InspectionClassification    == {"Inspected", "NotInspected"}
ManufacturingClassification == {"StandardManufacturing", "CustomManufacturing"}
ItemClassification          == {"StandardItem", "PremiumItem", "CustomMadeItem"}

(* ----------------------------------------------------------------------------
   オプショナル型: \cup {NULL} で表現
   ---------------------------------------------------------------------------- *)
NULL == "NULL"

(* ----------------------------------------------------------------------------
   ロット明細
   ---------------------------------------------------------------------------- *)
LotItem == [
    itemClassification        : ItemClassification,
    premiumClassification     : PremiumClassifications \cup {NULL},     \* 上位品区分?
    productClassificationCode : ProductClassificationCodes,
    lengthLowerSpec           : LengthLowerSpecs,
    thicknessLowerSpec        : ThicknessLowerSpecs,
    thicknessUpperSpec        : ThicknessUpperSpecs,
    gradeClassification       : GradeClassifications,
    count                     : Count,
    quantity                  : Quantity,
    inspectionResult          : InspectionResultClassifications \cup {NULL}  \* 検査結果区分?
]

(* ----------------------------------------------------------------------------
   ロット共通
   List<ロット明細> // 1件以上 → サイズ 1 以上の SUBSET
   ---------------------------------------------------------------------------- *)
LotCommon == [
    lotNumber                   : LotNumber,
    divisionCode                : DivisionCodes,
    departmentCode              : DepartmentCodes,
    sectionCode                 : SectionCodes,
    processClassification       : ProcessClassification,
    inspectionClassification    : InspectionClassification,
    manufacturingClassification : ManufacturingClassification,
    lotItems                    : { s \in SUBSET LotItem : Cardinality(s) >= 1 }
]

(* ----------------------------------------------------------------------------
   変換先情報
   ---------------------------------------------------------------------------- *)
ConversionTarget == [
    targetClassification : ItemClassification,
    reason               : Reasons
]

(* ----------------------------------------------------------------------------
   在庫ロット (タグ付き record で sum type を表現)
   各バリアントは state フィールドで識別される。
   ---------------------------------------------------------------------------- *)
ManufacturingLot == [
    state                      : {"Manufacturing"},
    common                     : LotCommon
]

ManufacturedLot == [
    state                      : {"Manufactured"},
    common                     : LotCommon,
    manufacturingCompletedDate : DateOnly
]

ShippingInstructedLot == [
    state                      : {"ShippingInstructed"},
    common                     : LotCommon,
    manufacturingCompletedDate : DateOnly,
    shippingDeadlineDate       : DateOnly
]

ShippedLot == [
    state                      : {"Shipped"},
    common                     : LotCommon,
    manufacturingCompletedDate : DateOnly,
    shippingDeadlineDate       : DateOnly,
    shippingDate               : DateOnly
]

ConversionInstructedLot == [
    state                      : {"ConversionInstructed"},
    common                     : LotCommon,
    manufacturingCompletedDate : DateOnly,
    conversionTarget           : ConversionTarget
]

InventoryLot ==
    ManufacturingLot
    \cup ManufacturedLot
    \cup ShippingInstructedLot
    \cup ShippedLot
    \cup ConversionInstructedLot

(* ============================================================================
   状態変数
   ============================================================================ *)

VARIABLES inventoryLots

vars == << inventoryLots >>

(* ============================================================================
   型不変条件 (基本的な safety)
   ============================================================================ *)
TypeOK == inventoryLots \subseteq InventoryLot

(* ============================================================================
   初期状態
   ============================================================================ *)
Init == inventoryLots = {}

(* ============================================================================
   アクション (DSL の behavior をそのまま TLA+ アクションに翻訳)
   ============================================================================ *)

(* behavior 製造完了を指示する = 製造中ロット AND 製造完了日 -> 製造完了ロット *)
CompleteManufacturing(lot, date) ==
    /\ lot \in inventoryLots
    /\ lot.state = "Manufacturing"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state                      |-> "Manufactured",
           common                     |-> lot.common,
           manufacturingCompletedDate |-> date ]}

(* behavior 出荷を指示する = 製造完了ロット AND 出荷期限日 -> 出荷指示済みロット *)
InstructShipping(lot, deadline) ==
    /\ lot \in inventoryLots
    /\ lot.state = "Manufactured"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state                      |-> "ShippingInstructed",
           common                     |-> lot.common,
           manufacturingCompletedDate |-> lot.manufacturingCompletedDate,
           shippingDeadlineDate       |-> deadline ]}

(* behavior 出荷完了を指示する = 出荷指示済みロット AND 出荷日 -> 出荷完了ロット *)
CompleteShipping(lot, date) ==
    /\ lot \in inventoryLots
    /\ lot.state = "ShippingInstructed"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state                      |-> "Shipped",
           common                     |-> lot.common,
           manufacturingCompletedDate |-> lot.manufacturingCompletedDate,
           shippingDeadlineDate       |-> lot.shippingDeadlineDate,
           shippingDate               |-> date ]}

(* behavior 製造完了を取り消す = 製造完了ロット -> 製造中ロット *)
CancelManufacturingCompletion(lot) ==
    /\ lot \in inventoryLots
    /\ lot.state = "Manufactured"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state  |-> "Manufacturing",
           common |-> lot.common ]}

(* behavior 品目変換を指示する = 製造完了ロット AND 変換先情報 -> 変換指示済みロット *)
InstructConversion(lot, target) ==
    /\ lot \in inventoryLots
    /\ lot.state = "Manufactured"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state                      |-> "ConversionInstructed",
           common                     |-> lot.common,
           manufacturingCompletedDate |-> lot.manufacturingCompletedDate,
           conversionTarget           |-> target ]}

(* behavior 品目変換指示を取り消す = 変換指示済みロット -> 製造完了ロット *)
CancelConversionInstruction(lot) ==
    /\ lot \in inventoryLots
    /\ lot.state = "ConversionInstructed"
    /\ inventoryLots' =
        (inventoryLots \ {lot}) \cup
        {[ state                      |-> "Manufactured",
           common                     |-> lot.common,
           manufacturingCompletedDate |-> lot.manufacturingCompletedDate ]}

(* ============================================================================
   次状態関係 (任意の lot に対していずれかのアクションが発火する)
   ============================================================================ *)
Next ==
    \E lot \in inventoryLots :
        \/ \E date     \in DateOnly         : CompleteManufacturing(lot, date)
        \/ \E deadline \in DateOnly         : InstructShipping(lot, deadline)
        \/ \E date     \in DateOnly         : CompleteShipping(lot, date)
        \/                                    CancelManufacturingCompletion(lot)
        \/ \E target   \in ConversionTarget : InstructConversion(lot, target)
        \/                                    CancelConversionInstruction(lot)

(* ============================================================================
   仕様
   ============================================================================ *)
Spec == Init /\ [][Next]_vars

=============================================================================
