-- ============================================================
-- 在庫ロットドメイン Alloy モデル (case-001 expected.als)
-- ============================================================
-- 出典: dsl/domain-model.md（在庫ロット部分）
-- 翻訳規則: harness/SEMANTICS.md の Alloy 章
--
-- スコープ: [CORE] サブセットのみ。
-- 値域制約 (where) と横断的不変条件 (invariant) は [VERIFICATION] のため
-- ここでは Amount / Count の最小限のみ含める（コメント慣例由来）。
-- ============================================================

module InventoryLot

-- ============================================================
-- 1. 値型（branded sigs / refined）
-- ============================================================

sig DivisionCode { value: Int }
sig DepartmentCode { value: Int }
sig SectionCode { value: Int }

-- 金額: 0以上
sig Amount { value: Int }
fact AmountNonNegative { all a: Amount | a.value >= 0 }

-- 数量: decimal 域。Alloy の Int では精度が表現できないため抽象化
sig Quantity {}

-- 個数: 1以上
sig Count { value: Int }
fact CountAtLeastOne { all c: Count | c.value >= 1 }

-- ============================================================
-- 2. ロット番号（複合キー）
-- DSL: data ロット番号 = ロット番号年度 AND ロット番号保管場所 AND ロット番号連番
-- ============================================================

sig LotNumberYear { value: Int }
sig LotNumberStorageLocation {}
sig LotNumberSequence { value: Int }

sig LotNumber {
    year: one LotNumberYear,
    storageLocation: one LotNumberStorageLocation,
    sequence: one LotNumberSequence
}

-- ============================================================
-- 3. 区分（OR を abstract sig + extends で表現）
-- ============================================================

abstract sig ProcessClassification {}
one sig InspectionProcess extends ProcessClassification {}
one sig ManufacturingProcess extends ProcessClassification {}
one sig ShippingProcess extends ProcessClassification {}

abstract sig InspectionClassification {}
one sig Inspected extends InspectionClassification {}
one sig NotInspected extends InspectionClassification {}

abstract sig ManufacturingClassification {}
one sig StandardManufacturing extends ManufacturingClassification {}
one sig CustomManufacturing extends ManufacturingClassification {}

-- 品目区分（DSL: 一般品 OR 上位品 OR 特注品）
abstract sig ItemClassification {}
one sig StandardItem extends ItemClassification {}      -- 一般品
one sig PremiumItem extends ItemClassification {}        -- 上位品
one sig CustomMadeItem extends ItemClassification {}     -- 特注品

-- ============================================================
-- 4. ロット明細
-- DSL: data ロット明細 = 品目区分 AND 上位品区分? AND ... AND 検査結果区分?
-- ============================================================

sig GradeClassification {}
sig LengthLowerSpec {}
sig ThicknessLowerSpec {}
sig ThicknessUpperSpec {}
sig ProductClassificationCode {}
sig PremiumClassification {}
sig InspectionResultClassification {}

sig LotItem {
    itemClassification: one ItemClassification,
    premiumClassification: lone PremiumClassification,       -- DSL: 上位品区分?
    productClassificationCode: one ProductClassificationCode,
    lengthLowerSpec: one LengthLowerSpec,
    thicknessLowerSpec: one ThicknessLowerSpec,
    thicknessUpperSpec: one ThicknessUpperSpec,
    gradeClassification: one GradeClassification,
    count: one Count,
    quantity: one Quantity,
    inspectionResult: lone InspectionResultClassification    -- DSL: 検査結果区分?
}

-- ============================================================
-- 5. ロット共通
-- DSL: ロット共通 = ロット番号 AND ... AND List<ロット明細> // 1件以上
-- ============================================================

sig LotCommon {
    lotNumber: one LotNumber,
    divisionCode: one DivisionCode,
    departmentCode: one DepartmentCode,
    sectionCode: one SectionCode,
    processClassification: one ProcessClassification,
    inspectionClassification: one InspectionClassification,
    manufacturingClassification: one ManufacturingClassification,
    lotItems: some LotItem                                   -- DSL: 1件以上
}

-- ============================================================
-- 6. 在庫ロットの状態（OR を abstract sig + extends）
-- DSL: 在庫ロット = 製造中ロット OR 製造完了ロット OR 出荷指示済みロット
--                  OR 出荷完了ロット OR 変換指示済みロット
-- ============================================================

sig DateOnly {}  -- 日付の抽象表現

sig ConversionTarget {
    targetClassification: one ItemClassification,
    reason: one Reason
}
sig Reason {}

abstract sig InventoryLot {
    common: one LotCommon
}

sig ManufacturingLot extends InventoryLot {}

sig ManufacturedLot extends InventoryLot {
    manufacturingCompletedDate: one DateOnly
}

sig ShippingInstructedLot extends InventoryLot {
    manufacturingCompletedDate: one DateOnly,
    shippingDeadlineDate: one DateOnly
}

sig ShippedLot extends InventoryLot {
    manufacturingCompletedDate: one DateOnly,
    shippingDeadlineDate: one DateOnly,
    shippingDate: one DateOnly
}

sig ConversionInstructedLot extends InventoryLot {
    manufacturingCompletedDate: one DateOnly,
    conversionTarget: one ConversionTarget
}

-- ============================================================
-- 7. 振る舞い (behavior) を pred で表現
-- DSL の behavior X = Input -> Output OR Error は
-- pred X[input, output] { 事後条件 } で表現する
-- ============================================================

-- behavior 製造完了を指示する = 製造中ロット AND 製造完了日 -> 製造完了ロット
pred completeManufacturing[lot: ManufacturingLot, date: DateOnly, result: ManufacturedLot] {
    result.common = lot.common
    result.manufacturingCompletedDate = date
}

-- behavior 出荷を指示する = 製造完了ロット AND 出荷期限日 -> 出荷指示済みロット
pred instructShipping[lot: ManufacturedLot, deadline: DateOnly, result: ShippingInstructedLot] {
    result.common = lot.common
    result.manufacturingCompletedDate = lot.manufacturingCompletedDate
    result.shippingDeadlineDate = deadline
}

-- behavior 出荷完了を指示する = 出荷指示済みロット AND 出荷日 -> 出荷完了ロット
pred completeShipping[lot: ShippingInstructedLot, date: DateOnly, result: ShippedLot] {
    result.common = lot.common
    result.manufacturingCompletedDate = lot.manufacturingCompletedDate
    result.shippingDeadlineDate = lot.shippingDeadlineDate
    result.shippingDate = date
}

-- behavior 製造完了を取り消す = 製造完了ロット -> 製造中ロット
pred cancelManufacturingCompletion[lot: ManufacturedLot, result: ManufacturingLot] {
    result.common = lot.common
}

-- behavior 品目変換を指示する = 製造完了ロット AND 変換先情報 -> 変換指示済みロット
pred instructConversion[lot: ManufacturedLot, target: ConversionTarget, result: ConversionInstructedLot] {
    result.common = lot.common
    result.manufacturingCompletedDate = lot.manufacturingCompletedDate
    result.conversionTarget = target
}

-- behavior 品目変換指示を取り消す = 変換指示済みロット -> 製造完了ロット
pred cancelConversionInstruction[lot: ConversionInstructedLot, result: ManufacturedLot] {
    result.common = lot.common
    result.manufacturingCompletedDate = lot.manufacturingCompletedDate
}

-- ============================================================
-- 8. 動作確認用 run コマンド
-- 各 behavior が成立するインスタンスが存在するか確認
-- ============================================================

run completeManufacturing for 4
run instructShipping for 4
run completeShipping for 4
run cancelManufacturingCompletion for 4
run instructConversion for 4
run cancelConversionInstruction for 4
