---------------------------- MODULE Pricing ----------------------------
(* ============================================================================
   価格査定・販売契約ドメイン TLA+ 仕様 (case-003 expected.tla)
   ============================================================================
   出典: dsl/domain-model.md（価格査定・販売契約部分）
   翻訳規則: harness/SEMANTICS.md の TLA+ 章

   スコープ: [CORE] のみ。case-003 input.dsl の behavior が対象。
   状態変数は salesCases （直接販売案件の状態を遷移させる）。
   ============================================================================ *)

EXTENDS Naturals, FiniteSets

CONSTANTS
    DateOnly,
    LotNumbers,                              (* case-001 *)
    LotItems,                                (* case-001 *)
    PreAppraisalDirectSalesCases,            (* case-002 *)
    AppraisedDirectSalesCases,               (* case-002 *)
    ContractedDirectSalesCases,              (* case-002 *)
    AppraisalInputs,                         (* 査定情報 *)
    ContractInputs,                          (* 契約情報 *)
    SalesMarkets,
    PersonInCharges,
    ItemDescriptions,
    DeliveryMethods,
    Usages,
    PaymentDeferralConditions,
    CustomerNumbers,
    CustomerContractNumbers,
    AgentNames,
    ReservationLotInfos,
    AppraisalNumberYears,
    AppraisalNumberMonths,
    AppraisalNumberSequences,
    ContractNumberYears,
    ContractNumberMonths,
    ContractNumberSequences,
    SalesTypes,
    SalesMethods

(* ----------------------------------------------------------------------------
   値型
   ---------------------------------------------------------------------------- *)
Amount == { x \in Nat : x >= 0 }

(* DSL に内部構造の記載がない数値系は Nat で抽象化 *)
MachiningCost == Nat
OrderSurcharge == Nat
GradeSurcharge == Nat
ReservationSurcharge == Nat
AdjustmentRate == Nat
QualityAdjustmentRate == Nat
ManufacturingCost == Nat
AssumedSalesPeriod == Nat
TargetProfitRate == Nat
BaseUnitPrice == Nat
PeriodAdjustmentRate == Nat
CounterpartyAdjustmentRate == Nat
SpecialPeriodAdjustmentRate == Nat
ContractAdjustmentRate == Nat

NULL == "NULL"

(* ----------------------------------------------------------------------------
   識別子
   ---------------------------------------------------------------------------- *)
AppraisalNumber == [
    year     : AppraisalNumberYears,
    month    : AppraisalNumberMonths,
    sequence : AppraisalNumberSequences
]

ContractNumber == [
    year     : ContractNumberYears,
    month    : ContractNumberMonths,
    sequence : ContractNumberSequences
]

(* ----------------------------------------------------------------------------
   ロット価格査定 / ロット明細価格査定
   ---------------------------------------------------------------------------- *)
LotItemPricing == [
    lotItem                     : LotItems,
    baseUnitPrice               : BaseUnitPrice,
    periodAdjustmentRate        : PeriodAdjustmentRate,
    counterpartyAdjustmentRate  : CounterpartyAdjustmentRate,
    specialPeriodAdjustmentRate : SpecialPeriodAdjustmentRate \cup {NULL}
]

LotPricing == [
    lotNumber             : LotNumbers,
    machiningCost         : MachiningCost \cup {NULL},
    orderSurcharge        : OrderSurcharge \cup {NULL},
    gradeSurcharge        : GradeSurcharge \cup {NULL},
    reservationSurcharge  : ReservationSurcharge \cup {NULL},
    adjustmentRate        : AdjustmentRate \cup {NULL},
    qualityAdjustmentRate : QualityAdjustmentRate \cup {NULL},
    manufacturingCost     : ManufacturingCost \cup {NULL},
    assumedSalesPeriod    : AssumedSalesPeriod \cup {NULL},
    targetProfitRate      : TargetProfitRate \cup {NULL},
    lotItemPricings       : { s \in SUBSET LotItemPricing : Cardinality(s) >= 1 }
]

(* ----------------------------------------------------------------------------
   価格査定
   ---------------------------------------------------------------------------- *)
AppraisalCommon == [
    appraisalNumber                      : AppraisalNumber,
    appraisalDate                        : DateOnly,
    deliveryDeadline                     : DateOnly,
    salesMarket                          : SalesMarkets,
    baseUnitPriceAppliedDate             : DateOnly,
    periodAdjustmentRateAppliedDate      : DateOnly,
    counterpartyAdjustmentRateAppliedDate: DateOnly,
    plannedTotalExcludingTax             : Amount,
    lotPricings                          : { s \in SUBSET LotPricing : Cardinality(s) >= 1 }
]

StandardAppraisal == [
    type   : {"Standard"},
    common : AppraisalCommon
]

CustomerContractAppraisal == [
    type                   : {"CustomerContract"},
    common                 : AppraisalCommon,
    customerContractNumber : CustomerContractNumbers,
    contractAdjustmentRate : ContractAdjustmentRate
]

Pricing == StandardAppraisal \cup CustomerContractAppraisal

(* ----------------------------------------------------------------------------
   予約価格
   ---------------------------------------------------------------------------- *)
ReservationPriceCommon == [
    appraisalNumber    : AppraisalNumber,
    appraisalDate      : DateOnly,
    reservationLotInfo : ReservationLotInfos,
    reservationAmount  : Amount
]

TentativeReservationPrice == [
    type   : {"Tentative"},
    common : ReservationPriceCommon
]

ConfirmedReservationPrice == [
    type            : {"Confirmed"},
    common          : ReservationPriceCommon,
    confirmedDate   : DateOnly,
    confirmedAmount : Amount
]

ReservationPrice == TentativeReservationPrice \cup ConfirmedReservationPrice

(* ----------------------------------------------------------------------------
   販売契約
   ---------------------------------------------------------------------------- *)
Purchaser == [
    customerNumber : CustomerNumbers,
    agentName      : AgentNames \cup {NULL}      \* 代理人氏名?
]

SalesInformation == [
    salesType                : SalesTypes,
    item                     : ItemDescriptions,
    deliveryMethod           : DeliveryMethods,
    paymentDeferralCondition : PaymentDeferralConditions \cup {NULL},
    salesMethod              : SalesMethods,
    usage                    : Usages \cup {NULL},
    paymentDeferralAmount    : Amount \cup {NULL}
]

SalesPriceInformation == [
    contractAmountExcludingTax : Amount,
    consumptionTax             : Amount,
    paidAmountExcludingTax     : Amount,
    paidConsumptionTax         : Amount
]

SalesContract == [
    contractNumber        : ContractNumber,
    contractDate          : DateOnly,
    personInCharge        : PersonInCharges,
    purchaser             : Purchaser,
    salesInformation      : SalesInformation,
    salesPriceInformation : SalesPriceInformation,
    appraisalNumber       : AppraisalNumber
]

(* ============================================================================
   状態変数（直接販売案件の集合を遷移させる）
   ============================================================================ *)
VARIABLES salesCases
vars == << salesCases >>

DirectSalesCaseLike ==
    PreAppraisalDirectSalesCases
    \cup AppraisedDirectSalesCases
    \cup ContractedDirectSalesCases

TypeOK == salesCases \subseteq DirectSalesCaseLike

Init == salesCases = {}

(* ============================================================================
   アクション
   ============================================================================ *)

CreateAppraisal(case_, input, appraised) ==
    /\ case_ \in salesCases
    /\ case_ \in PreAppraisalDirectSalesCases
    /\ appraised \in AppraisedDirectSalesCases
    /\ salesCases' = (salesCases \ {case_}) \cup {appraised}

UpdateAppraisal(case_, input, updated) ==
    /\ case_ \in salesCases
    /\ case_ \in AppraisedDirectSalesCases
    /\ updated \in AppraisedDirectSalesCases
    /\ salesCases' = (salesCases \ {case_}) \cup {updated}

DeleteAppraisal(case_, preAppraisal) ==
    /\ case_ \in salesCases
    /\ case_ \in AppraisedDirectSalesCases
    /\ preAppraisal \in PreAppraisalDirectSalesCases
    /\ salesCases' = (salesCases \ {case_}) \cup {preAppraisal}

ConcludeSalesContract(case_, input, contracted) ==
    /\ case_ \in salesCases
    /\ case_ \in AppraisedDirectSalesCases
    /\ contracted \in ContractedDirectSalesCases
    /\ salesCases' = (salesCases \ {case_}) \cup {contracted}

DeleteSalesContract(case_, appraised) ==
    /\ case_ \in salesCases
    /\ case_ \in ContractedDirectSalesCases
    /\ appraised \in AppraisedDirectSalesCases
    /\ salesCases' = (salesCases \ {case_}) \cup {appraised}

Next ==
    \E case_ \in salesCases :
        \/ \E inp \in AppraisalInputs, app \in AppraisedDirectSalesCases :
              CreateAppraisal(case_, inp, app)
        \/ \E inp \in AppraisalInputs, app \in AppraisedDirectSalesCases :
              UpdateAppraisal(case_, inp, app)
        \/ \E pre \in PreAppraisalDirectSalesCases :
              DeleteAppraisal(case_, pre)
        \/ \E inp \in ContractInputs, ctr \in ContractedDirectSalesCases :
              ConcludeSalesContract(case_, inp, ctr)
        \/ \E app \in AppraisedDirectSalesCases :
              DeleteSalesContract(case_, app)

Spec == Init /\ [][Next]_vars

=============================================================================
