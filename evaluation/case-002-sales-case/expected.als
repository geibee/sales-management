-- ============================================================
-- 販売案件ドメイン Alloy モデル (case-002 expected.als)
-- ============================================================
-- 出典: dsl/domain-model.md（販売案件部分）
-- 翻訳規則: harness/SEMANTICS.md の Alloy 章
--
-- スコープ: [CORE] のみ。
-- 在庫ロット領域の sig (InventoryLot, ManufacturedLot, DivisionCode 等)
-- および 価格査定領域の sig (Pricing, SalesContract, ReservationPrice) は
-- 別ファイル（case-001 / case-003）で定義された前提とする。
-- ここでは抽象 sig として宣言する。
-- ============================================================

module SalesCase

-- ============================================================
-- 1. 外部参照（他ケースで定義される sig の placeholder）
-- ============================================================

sig InventoryLot {}            -- case-001
sig ManufacturedLot extends InventoryLot {}  -- case-001
sig DivisionCode {}            -- case-001
sig DateOnly {}                -- case-001
sig Pricing {}                 -- case-003
sig SalesContract {}           -- case-003
sig ReservationPrice {}        -- case-003
sig ConsignedAgentInfo {}      -- 委託業者情報（DSL 内では未定義）
sig ConsignmentResult {}       -- 委託販売結果（DSL 内では未定義）
sig SalesCaseKind {}           -- 販売案件種別（DSL 内では未定義）
sig ReservationPriceInput {}   -- 予約価格情報（DSL 内では未定義）

-- ============================================================
-- 2. 識別子
-- DSL: data 販売案件番号 = 販売案件番号年度 AND 販売案件番号月度 AND 販売案件番号連番
-- ============================================================

sig SalesCaseNumberYear { value: Int }
sig SalesCaseNumberMonth { value: Int }
sig SalesCaseNumberSequence { value: Int }

sig SalesCaseNumber {
    year     : one SalesCaseNumberYear,
    month    : one SalesCaseNumberMonth,
    sequence : one SalesCaseNumberSequence
}

-- ============================================================
-- 3. 出荷指示情報
-- DSL: data 出荷指示情報 = 出荷指示日
-- ============================================================

sig ShippingInstruction {
    shippingInstructionDate: one DateOnly
}

-- ============================================================
-- 4. 販売案件共通
-- DSL: data 販売案件共通 = 販売案件番号 AND 事業部コード AND 販売実施日
--                          AND List<在庫ロット> // 1件以上
-- ============================================================

sig SalesCaseCommon {
    salesCaseNumber : one SalesCaseNumber,
    divisionCode    : one DivisionCode,
    salesDate       : one DateOnly,
    inventoryLots   : some InventoryLot
}

-- ============================================================
-- 5. 直接販売案件
-- ============================================================

abstract sig DirectSalesCase {
    common: one SalesCaseCommon
}

sig PreAppraisalDirectSalesCase extends DirectSalesCase {}

sig AppraisedDirectSalesCase extends DirectSalesCase {
    pricing: one Pricing
}

sig ContractedDirectSalesCase extends DirectSalesCase {
    pricing       : one Pricing,
    salesContract : one SalesContract
}

sig ShippingInstructedDirectSalesCase extends DirectSalesCase {
    pricing             : one Pricing,
    salesContract       : one SalesContract,
    shippingInstruction : one ShippingInstruction
}

sig ShippedDirectSalesCase extends DirectSalesCase {
    pricing             : one Pricing,
    salesContract       : one SalesContract,
    shippingInstruction : one ShippingInstruction,
    shippingDate        : one DateOnly
}

-- ============================================================
-- 6. 予約販売案件
-- ============================================================

abstract sig ReservationSalesCase {
    common: one SalesCaseCommon
}

sig TentativeReservationCase extends ReservationSalesCase {}

sig ReservedCase extends ReservationSalesCase {
    reservationPrice: one ReservationPrice
}

sig ConfirmedReservationCase extends ReservationSalesCase {
    reservationPrice : one ReservationPrice,
    confirmedDate    : one DateOnly
}

sig DeliveredReservationCase extends ReservationSalesCase {
    reservationPrice : one ReservationPrice,
    confirmedDate    : one DateOnly,
    deliveredDate    : one DateOnly
}

-- ============================================================
-- 7. 委託販売案件
-- ============================================================

abstract sig ConsignmentSalesCase {
    common: one SalesCaseCommon
}

sig PreAssignmentConsignmentCase extends ConsignmentSalesCase {}

sig AssignedConsignmentCase extends ConsignmentSalesCase {
    agentInfo: one ConsignedAgentInfo
}

sig ResultEnteredConsignmentCase extends ConsignmentSalesCase {
    agentInfo : one ConsignedAgentInfo,
    result    : one ConsignmentResult
}

-- ============================================================
-- 8. 販売案件 (最上位の OR)
-- DSL: data 販売案件 = 直接販売案件 OR 予約販売案件 OR 委託販売案件
-- ============================================================

abstract sig SalesCase {}
sig DirectSalesCaseRef extends SalesCase     { sub: one DirectSalesCase }
sig ReservationSalesCaseRef extends SalesCase { sub: one ReservationSalesCase }
sig ConsignmentSalesCaseRef extends SalesCase  { sub: one ConsignmentSalesCase }

-- ============================================================
-- 9. 振る舞い (案件作成・削除と各サブドメインのライフサイクル)
-- ============================================================

-- behavior 出庫を指示する = 契約済み直接販売案件 -> 出荷指示済み直接販売案件
pred instructShipping[
    case_: ContractedDirectSalesCase,
    info: ShippingInstruction,
    result: ShippingInstructedDirectSalesCase
] {
    result.common              = case_.common
    result.pricing             = case_.pricing
    result.salesContract       = case_.salesContract
    result.shippingInstruction = info
}

-- behavior 出庫完了を指示する = 出荷指示済み直接販売案件 AND 出荷完了日 -> 出荷完了直接販売案件
pred completeShipping[
    case_: ShippingInstructedDirectSalesCase,
    date: DateOnly,
    result: ShippedDirectSalesCase
] {
    result.common              = case_.common
    result.pricing             = case_.pricing
    result.salesContract       = case_.salesContract
    result.shippingInstruction = case_.shippingInstruction
    result.shippingDate        = date
}

-- behavior 出庫指示を取り消す = 出荷指示済み直接販売案件 -> 契約済み直接販売案件
pred cancelShippingInstruction[
    case_: ShippingInstructedDirectSalesCase,
    result: ContractedDirectSalesCase
] {
    result.common        = case_.common
    result.pricing       = case_.pricing
    result.salesContract = case_.salesContract
}

-- behavior 予約価格を作成する = 仮予約案件 AND 予約価格情報 -> 予約済み案件
pred createReservationPrice[
    case_: TentativeReservationCase,
    input: ReservationPriceInput,
    result: ReservedCase
] {
    result.common = case_.common
}

-- behavior 予約を確定する = 予約済み案件 AND 確定日 -> 予約確定済み案件
pred confirmReservation[
    case_: ReservedCase,
    date: DateOnly,
    result: ConfirmedReservationCase
] {
    result.common           = case_.common
    result.reservationPrice = case_.reservationPrice
    result.confirmedDate    = date
}

-- behavior 予約確定を取り消す = 予約確定済み案件 -> 予約済み案件
pred cancelReservationConfirmation[
    case_: ConfirmedReservationCase,
    result: ReservedCase
] {
    result.common           = case_.common
    result.reservationPrice = case_.reservationPrice
}

-- behavior 納品を指示する = 予約確定済み案件 AND 納品日 -> 予約納品済み案件
pred instructDelivery[
    case_: ConfirmedReservationCase,
    date: DateOnly,
    result: DeliveredReservationCase
] {
    result.common           = case_.common
    result.reservationPrice = case_.reservationPrice
    result.confirmedDate    = case_.confirmedDate
    result.deliveredDate    = date
}

-- behavior 委託販売案件を指定する = 委託指定前販売案件 AND 委託業者情報 -> 委託指定済み販売案件
pred assignConsignment[
    case_: PreAssignmentConsignmentCase,
    info: ConsignedAgentInfo,
    result: AssignedConsignmentCase
] {
    result.common    = case_.common
    result.agentInfo = info
}

-- behavior 委託販売案件指定を解除する = 委託指定済み販売案件 -> 委託指定前販売案件
pred cancelConsignmentAssignment[
    case_: AssignedConsignmentCase,
    result: PreAssignmentConsignmentCase
] {
    result.common = case_.common
}

-- behavior 委託販売結果を入力する = 委託指定済み販売案件 AND 委託販売結果 -> 委託販売結果入力済み販売案件
pred enterConsignmentResult[
    case_: AssignedConsignmentCase,
    consignmentResult: ConsignmentResult,
    result: ResultEnteredConsignmentCase
] {
    result.common    = case_.common
    result.agentInfo = case_.agentInfo
    result.result    = consignmentResult
}

-- ============================================================
-- 10. 動作確認用 run コマンド
-- ============================================================

run instructShipping for 4
run completeShipping for 4
run createReservationPrice for 4
run confirmReservation for 4
run assignConsignment for 4
run enterConsignmentResult for 4
