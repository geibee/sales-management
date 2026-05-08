---------------------------- MODULE SalesCase ----------------------------
(* ============================================================================
   販売案件ドメイン TLA+ 仕様 (case-002 expected.tla)
   ============================================================================
   出典: dsl/domain-model.md（販売案件部分）
   翻訳規則: harness/SEMANTICS.md の TLA+ 章

   スコープ: [CORE] のみ。case-002 input.dsl の behavior が対象。
   査定/契約系の behavior（case-003）はこのアクション群には含まれない。
   ============================================================================ *)

EXTENDS Naturals, FiniteSets

CONSTANTS
    DateOnly,
    DivisionCodes,
    InventoryLots,                  (* case-001 で定義 *)
    Pricings,                       (* case-003 で定義 *)
    SalesContracts,                 (* case-003 で定義 *)
    ReservationPrices,              (* case-003 で定義 *)
    ConsignedAgentInfos,            (* DSL 内で未定義 *)
    ConsignmentResults,             (* DSL 内で未定義 *)
    ReservationPriceInputs,         (* DSL 内で未定義 *)
    SalesCaseNumberYears,
    SalesCaseNumberMonths,
    SalesCaseNumberSequences

(* ----------------------------------------------------------------------------
   識別子
   ---------------------------------------------------------------------------- *)
SalesCaseNumber == [
    year     : SalesCaseNumberYears,
    month    : SalesCaseNumberMonths,
    sequence : SalesCaseNumberSequences
]

(* ----------------------------------------------------------------------------
   出荷指示情報
   ---------------------------------------------------------------------------- *)
ShippingInstruction == [
    shippingInstructionDate : DateOnly
]

(* ----------------------------------------------------------------------------
   販売案件共通
   ---------------------------------------------------------------------------- *)
SalesCaseCommon == [
    salesCaseNumber : SalesCaseNumber,
    divisionCode    : DivisionCodes,
    salesDate       : DateOnly,
    inventoryLots   : { s \in SUBSET InventoryLots : Cardinality(s) >= 1 }
]

(* ============================================================================
   状態のタグ付き record
   ============================================================================ *)

(* --- 直接販売案件 --- *)
PreAppraisalDirectSalesCase == [
    state  : {"PreAppraisalDirect"},
    common : SalesCaseCommon
]

AppraisedDirectSalesCase == [
    state   : {"AppraisedDirect"},
    common  : SalesCaseCommon,
    pricing : Pricings
]

ContractedDirectSalesCase == [
    state         : {"ContractedDirect"},
    common        : SalesCaseCommon,
    pricing       : Pricings,
    salesContract : SalesContracts
]

ShippingInstructedDirectSalesCase == [
    state               : {"ShippingInstructedDirect"},
    common              : SalesCaseCommon,
    pricing             : Pricings,
    salesContract       : SalesContracts,
    shippingInstruction : ShippingInstruction
]

ShippedDirectSalesCase == [
    state               : {"ShippedDirect"},
    common              : SalesCaseCommon,
    pricing             : Pricings,
    salesContract       : SalesContracts,
    shippingInstruction : ShippingInstruction,
    shippingDate        : DateOnly
]

DirectSalesCase ==
    PreAppraisalDirectSalesCase
    \cup AppraisedDirectSalesCase
    \cup ContractedDirectSalesCase
    \cup ShippingInstructedDirectSalesCase
    \cup ShippedDirectSalesCase

(* --- 予約販売案件 --- *)
TentativeReservationCase == [
    state  : {"TentativeReservation"},
    common : SalesCaseCommon
]

ReservedCase == [
    state            : {"Reserved"},
    common           : SalesCaseCommon,
    reservationPrice : ReservationPrices
]

ConfirmedReservationCase == [
    state            : {"ConfirmedReservation"},
    common           : SalesCaseCommon,
    reservationPrice : ReservationPrices,
    confirmedDate    : DateOnly
]

DeliveredReservationCase == [
    state            : {"DeliveredReservation"},
    common           : SalesCaseCommon,
    reservationPrice : ReservationPrices,
    confirmedDate    : DateOnly,
    deliveredDate    : DateOnly
]

ReservationSalesCase ==
    TentativeReservationCase
    \cup ReservedCase
    \cup ConfirmedReservationCase
    \cup DeliveredReservationCase

(* --- 委託販売案件 --- *)
PreAssignmentConsignmentCase == [
    state  : {"PreAssignmentConsignment"},
    common : SalesCaseCommon
]

AssignedConsignmentCase == [
    state     : {"AssignedConsignment"},
    common    : SalesCaseCommon,
    agentInfo : ConsignedAgentInfos
]

ResultEnteredConsignmentCase == [
    state     : {"ResultEnteredConsignment"},
    common    : SalesCaseCommon,
    agentInfo : ConsignedAgentInfos,
    result    : ConsignmentResults
]

ConsignmentSalesCase ==
    PreAssignmentConsignmentCase
    \cup AssignedConsignmentCase
    \cup ResultEnteredConsignmentCase

(* --- 販売案件 (最上位) --- *)
SalesCase == DirectSalesCase \cup ReservationSalesCase \cup ConsignmentSalesCase

(* ============================================================================
   状態変数
   ============================================================================ *)
VARIABLES salesCases

vars == << salesCases >>

TypeOK == salesCases \subseteq SalesCase

Init == salesCases = {}

(* ============================================================================
   アクション (case-002 input.dsl の behavior のみ)
   ============================================================================ *)

(* behavior 出庫を指示する *)
InstructShipping(case_, info) ==
    /\ case_ \in salesCases
    /\ case_.state = "ContractedDirect"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state               |-> "ShippingInstructedDirect",
           common              |-> case_.common,
           pricing             |-> case_.pricing,
           salesContract       |-> case_.salesContract,
           shippingInstruction |-> info ]}

(* behavior 出庫完了を指示する *)
CompleteShipping(case_, date) ==
    /\ case_ \in salesCases
    /\ case_.state = "ShippingInstructedDirect"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state               |-> "ShippedDirect",
           common              |-> case_.common,
           pricing             |-> case_.pricing,
           salesContract       |-> case_.salesContract,
           shippingInstruction |-> case_.shippingInstruction,
           shippingDate        |-> date ]}

(* behavior 出庫指示を取り消す *)
CancelShippingInstruction(case_) ==
    /\ case_ \in salesCases
    /\ case_.state = "ShippingInstructedDirect"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state         |-> "ContractedDirect",
           common        |-> case_.common,
           pricing       |-> case_.pricing,
           salesContract |-> case_.salesContract ]}

(* behavior 予約価格を作成する *)
CreateReservationPrice(case_, input, price) ==
    /\ case_ \in salesCases
    /\ case_.state = "TentativeReservation"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state            |-> "Reserved",
           common           |-> case_.common,
           reservationPrice |-> price ]}

(* behavior 予約を確定する *)
ConfirmReservation(case_, date) ==
    /\ case_ \in salesCases
    /\ case_.state = "Reserved"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state            |-> "ConfirmedReservation",
           common           |-> case_.common,
           reservationPrice |-> case_.reservationPrice,
           confirmedDate    |-> date ]}

(* behavior 予約確定を取り消す *)
CancelReservationConfirmation(case_) ==
    /\ case_ \in salesCases
    /\ case_.state = "ConfirmedReservation"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state            |-> "Reserved",
           common           |-> case_.common,
           reservationPrice |-> case_.reservationPrice ]}

(* behavior 納品を指示する *)
InstructDelivery(case_, date) ==
    /\ case_ \in salesCases
    /\ case_.state = "ConfirmedReservation"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state            |-> "DeliveredReservation",
           common           |-> case_.common,
           reservationPrice |-> case_.reservationPrice,
           confirmedDate    |-> case_.confirmedDate,
           deliveredDate    |-> date ]}

(* behavior 委託販売案件を指定する *)
AssignConsignment(case_, info) ==
    /\ case_ \in salesCases
    /\ case_.state = "PreAssignmentConsignment"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state     |-> "AssignedConsignment",
           common    |-> case_.common,
           agentInfo |-> info ]}

(* behavior 委託販売案件指定を解除する *)
CancelConsignmentAssignment(case_) ==
    /\ case_ \in salesCases
    /\ case_.state = "AssignedConsignment"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state  |-> "PreAssignmentConsignment",
           common |-> case_.common ]}

(* behavior 委託販売結果を入力する *)
EnterConsignmentResult(case_, result) ==
    /\ case_ \in salesCases
    /\ case_.state = "AssignedConsignment"
    /\ salesCases' =
        (salesCases \ {case_}) \cup
        {[ state     |-> "ResultEnteredConsignment",
           common    |-> case_.common,
           agentInfo |-> case_.agentInfo,
           result    |-> result ]}

(* ============================================================================
   次状態関係
   ============================================================================ *)
Next ==
    \E case_ \in salesCases :
        \/ \E info  \in ShippingInstruction    : InstructShipping(case_, info)
        \/ \E date  \in DateOnly               : CompleteShipping(case_, date)
        \/                                       CancelShippingInstruction(case_)
        \/ \E inp \in ReservationPriceInputs,
              p   \in ReservationPrices        : CreateReservationPrice(case_, inp, p)
        \/ \E date  \in DateOnly               : ConfirmReservation(case_, date)
        \/                                       CancelReservationConfirmation(case_)
        \/ \E date  \in DateOnly               : InstructDelivery(case_, date)
        \/ \E info  \in ConsignedAgentInfos    : AssignConsignment(case_, info)
        \/                                       CancelConsignmentAssignment(case_)
        \/ \E r     \in ConsignmentResults     : EnterConsignmentResult(case_, r)

Spec == Init /\ [][Next]_vars

=============================================================================
