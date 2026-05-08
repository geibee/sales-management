-- ============================================================
-- 価格査定・販売契約ドメイン Alloy モデル (case-003 expected.als)
-- ============================================================
-- 出典: dsl/domain-model.md（価格査定・販売契約部分）
-- 翻訳規則: harness/SEMANTICS.md の Alloy 章
--
-- スコープ: [CORE] のみ。
-- 在庫ロット領域 (LotNumber, LotItem) と販売案件領域
-- (PreAppraisalDirectSalesCase 等) は別ファイルで定義された前提とし
-- プレースホルダ sig として宣言する。
-- ============================================================

module Pricing

-- ============================================================
-- 1. 外部参照（プレースホルダ）
-- ============================================================

sig LotNumber {}                        -- case-001
sig LotItem {}                          -- case-001
sig DateOnly {}                         -- case-001
sig Amount {}                           -- case-001 流用または独自定義
sig PreAppraisalDirectSalesCase {}      -- case-002
sig AppraisedDirectSalesCase {}         -- case-002
sig ContractedDirectSalesCase {}        -- case-002
sig AppraisalInput {}                   -- 査定情報（DSL 内未定義）
sig ContractInput {}                    -- 契約情報（DSL 内未定義）

-- DSL 内で型として宣言されない業務概念
sig SalesMarket {}                      -- 販売市場
sig PersonInCharge {}                   -- 担当者
sig ItemDescription {}                  -- 品目
sig DeliveryMethod {}                   -- 納入方法
sig Usage {}                            -- 用途
sig PaymentDeferralCondition {}         -- 支払猶予条件
sig CustomerNumber {}                   -- 顧客番号
sig CustomerContractNumber {}           -- 顧客契約番号
sig AgentName {}                        -- 代理人氏名
sig ReservationLotInfo {}               -- 予約対象ロット情報
sig MachiningCost {}                    -- 加工費
sig OrderSurcharge {}                   -- 個別受注加算
sig GradeSurcharge {}                   -- 等級加算
sig ReservationSurcharge {}             -- 予約加算
sig AdjustmentRate {}                   -- 調整率
sig QualityAdjustmentRate {}            -- 品質調整率
sig ManufacturingCost {}                -- 製造費単価
sig AssumedSalesPeriod {}               -- 想定販売期間
sig TargetProfitRate {}                 -- 目標利益率
sig BaseUnitPrice {}                    -- 基準単価
sig PeriodAdjustmentRate {}             -- 期間調整率
sig CounterpartyAdjustmentRate {}       -- 取引先調整率
sig SpecialPeriodAdjustmentRate {}      -- 特例期間調整率
sig ContractAdjustmentRate {}           -- 契約調整率

-- ============================================================
-- 2. 識別子
-- ============================================================

sig AppraisalNumberYear { value: Int }
sig AppraisalNumberMonth { value: Int }
sig AppraisalNumberSequence { value: Int }
sig AppraisalNumber {
    year     : one AppraisalNumberYear,
    month    : one AppraisalNumberMonth,
    sequence : one AppraisalNumberSequence
}

sig ContractNumberYear { value: Int }
sig ContractNumberMonth { value: Int }
sig ContractNumberSequence { value: Int }
sig ContractNumber {
    year     : one ContractNumberYear,
    month    : one ContractNumberMonth,
    sequence : one ContractNumberSequence
}

-- ============================================================
-- 3. ロット明細価格査定 / ロット価格査定
-- ============================================================

sig LotItemPricing {
    lotItem                     : one LotItem,
    baseUnitPrice               : one BaseUnitPrice,
    periodAdjustmentRate        : one PeriodAdjustmentRate,
    counterpartyAdjustmentRate  : one CounterpartyAdjustmentRate,
    specialPeriodAdjustmentRate : lone SpecialPeriodAdjustmentRate
}

sig LotPricing {
    lotNumber             : one LotNumber,
    machiningCost         : lone MachiningCost,
    orderSurcharge        : lone OrderSurcharge,
    gradeSurcharge        : lone GradeSurcharge,
    reservationSurcharge  : lone ReservationSurcharge,
    adjustmentRate        : lone AdjustmentRate,
    qualityAdjustmentRate : lone QualityAdjustmentRate,
    manufacturingCost     : lone ManufacturingCost,
    assumedSalesPeriod    : lone AssumedSalesPeriod,
    targetProfitRate      : lone TargetProfitRate,
    lotItemPricings       : some LotItemPricing
}

-- ============================================================
-- 4. 価格査定
-- DSL: data 価格査定 = 通常査定 OR 顧客契約査定
-- ============================================================

sig AppraisalCommon {
    appraisalNumber                 : one AppraisalNumber,
    appraisalDate                   : one DateOnly,
    deliveryDeadline                : one DateOnly,
    salesMarket                     : one SalesMarket,
    baseUnitPriceAppliedDate        : one DateOnly,
    periodAdjustmentRateAppliedDate : one DateOnly,
    counterpartyAdjustmentRateAppliedDate : one DateOnly,
    plannedTotalExcludingTax        : one Amount,
    lotPricings                     : some LotPricing
}

abstract sig Pricing {}

sig StandardAppraisal extends Pricing {
    common: one AppraisalCommon
}

sig CustomerContractAppraisal extends Pricing {
    common                  : one AppraisalCommon,
    customerContractNumber  : one CustomerContractNumber,
    contractAdjustmentRate  : one ContractAdjustmentRate
}

-- ============================================================
-- 5. 予約価格
-- ============================================================

sig ReservationPriceCommon {
    appraisalNumber      : one AppraisalNumber,
    appraisalDate        : one DateOnly,
    reservationLotInfo   : one ReservationLotInfo,
    reservationAmount    : one Amount
}

abstract sig ReservationPrice {}

sig TentativeReservationPrice extends ReservationPrice {
    common: one ReservationPriceCommon
}

sig ConfirmedReservationPrice extends ReservationPrice {
    common          : one ReservationPriceCommon,
    confirmedDate   : one DateOnly,
    confirmedAmount : one Amount
}

-- ============================================================
-- 6. 販売契約
-- ============================================================

sig Purchaser {
    customerNumber : one CustomerNumber,
    agentName      : lone AgentName        -- 代理人氏名?
}

sig SalesInformation {
    salesType             : one SalesType,
    item                  : one ItemDescription,
    deliveryMethod        : one DeliveryMethod,
    paymentDeferralCondition : lone PaymentDeferralCondition,
    salesMethod           : one SalesMethod,
    usage                 : lone Usage,
    paymentDeferralAmount : lone Amount
}

abstract sig SalesType {}                -- 販売種別: DSL は整数。具体値域は任せる
abstract sig SalesMethod {}              -- 販売方式: 同上

sig SalesPriceInformation {
    contractAmountExcludingTax : one Amount,
    consumptionTax             : one Amount,
    paidAmountExcludingTax     : one Amount,
    paidConsumptionTax         : one Amount
}

sig SalesContract {
    contractNumber       : one ContractNumber,
    contractDate         : one DateOnly,
    personInCharge       : one PersonInCharge,
    purchaser            : one Purchaser,
    salesInformation     : one SalesInformation,
    salesPriceInformation: one SalesPriceInformation,
    appraisalNumber      : one AppraisalNumber  -- 紐づく価格査定
}

-- ============================================================
-- 7. 振る舞い
-- ============================================================

-- behavior 価格査定を作成する = 査定前直接販売案件 AND 査定情報 -> 査定済み直接販売案件
pred createAppraisal[
    case_: PreAppraisalDirectSalesCase,
    input: AppraisalInput,
    result: AppraisedDirectSalesCase
] {
    -- 事後条件は実装側で詳細化
}

-- behavior 価格査定を更新する = 査定済み直接販売案件 AND 査定情報 -> 査定済み直接販売案件
pred updateAppraisal[
    case_: AppraisedDirectSalesCase,
    input: AppraisalInput,
    result: AppraisedDirectSalesCase
] {}

-- behavior 価格査定を削除する = 査定済み直接販売案件 -> 査定前直接販売案件
pred deleteAppraisal[
    case_: AppraisedDirectSalesCase,
    result: PreAppraisalDirectSalesCase
] {}

-- behavior 販売契約を締結する = 査定済み直接販売案件 AND 契約情報 -> 契約済み直接販売案件
pred concludeSalesContract[
    case_: AppraisedDirectSalesCase,
    input: ContractInput,
    result: ContractedDirectSalesCase
] {}

-- behavior 販売契約を削除する = 契約済み直接販売案件 -> 査定済み直接販売案件
pred deleteSalesContract[
    case_: ContractedDirectSalesCase,
    result: AppraisedDirectSalesCase
] {}

-- ============================================================
-- 8. 動作確認用 run コマンド
-- ============================================================

run createAppraisal for 4
run updateAppraisal for 4
run concludeSalesContract for 4
