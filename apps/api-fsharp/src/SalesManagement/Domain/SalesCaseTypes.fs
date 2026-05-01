module SalesManagement.Domain.SalesCaseTypes

open System
open SalesManagement.Domain.Types

type SalesCaseNumber = { Year: int; Month: int; Seq: int }

type AppraisalNumber = { Year: int; Month: int; Seq: int }

type ContractNumber = { Year: int; Month: int; Seq: int }

type SalesCaseCommon =
    { SalesCaseNumber: SalesCaseNumber
      DivisionCode: DivisionCode
      SalesDate: DateOnly
      Lots: NonEmptyList<InventoryLot> }

type LotDetailAppraisal =
    { LotDetail: LotDetail
      BaseUnitPrice: Amount
      PeriodAdjustmentRate: decimal
      CounterpartyAdjustmentRate: decimal
      ExceptionalPeriodAdjustmentRate: decimal option }

type LotAppraisal =
    { LotNumber: LotNumber
      ProcessingCost: Amount option
      IndividualOrderPremium: decimal option
      GradePremium: decimal option
      ReservationAddon: decimal option
      AdjustmentRate: decimal option
      QualityAdjustmentRate: decimal option
      ManufacturingUnitCost: Amount option
      ExpectedSalesPeriod: int option
      TargetProfitRate: decimal option
      DetailAppraisals: NonEmptyList<LotDetailAppraisal> }

type AppraisalCommon =
    { AppraisalNumber: AppraisalNumber
      AppraisalDate: DateOnly
      DeliveryDate: DateOnly
      SalesMarket: string
      BaseUnitPriceDate: string
      PeriodAdjustmentRateDate: string
      CounterpartyAdjustmentRateDate: string
      TaxExcludedEstimatedTotal: Amount
      LotAppraisals: NonEmptyList<LotAppraisal> }

type NormalAppraisal = { Common: AppraisalCommon }

type CustomerContractAppraisal =
    { Common: AppraisalCommon
      CustomerContractNumber: string
      ContractAdjustmentRate: decimal }

type PriceAppraisal =
    | Normal of NormalAppraisal
    | CustomerContract of CustomerContractAppraisal

type Buyer =
    { CustomerNumber: string
      AgentName: string option }

type SalesInfo =
    { SalesType: int
      Item: string
      DeliveryMethod: string
      PaymentDeferralCondition: string option
      SalesMethod: int
      Usage: string option
      PaymentDeferralAmount: Amount option }

type SalesPriceInfo =
    { TaxExcludedContractAmount: Amount
      ConsumptionTax: Amount
      TaxExcludedPaymentAmount: Amount
      PaymentConsumptionTax: Amount }

type SalesContract =
    { ContractNumber: ContractNumber
      ContractDate: DateOnly
      Person: string
      Buyer: Buyer
      SalesInfo: SalesInfo
      SalesPriceInfo: SalesPriceInfo
      AppraisalNumber: AppraisalNumber }

type ShippingInstructionInfo = { ShippingInstructionDate: DateOnly }

type BeforeAppraisalCase = { Common: SalesCaseCommon }

type AppraisedCase =
    { Common: SalesCaseCommon
      Appraisal: PriceAppraisal }

type ContractedCase =
    { Common: SalesCaseCommon
      Appraisal: PriceAppraisal
      Contract: SalesContract }

type ShippingInstructedCase =
    { Common: SalesCaseCommon
      Appraisal: PriceAppraisal
      Contract: SalesContract
      ShippingInstruction: ShippingInstructionInfo }

type ShippingCompletedCase =
    { Common: SalesCaseCommon
      Appraisal: PriceAppraisal
      Contract: SalesContract
      ShippingInstruction: ShippingInstructionInfo
      ShippingCompletedDate: DateOnly }

type DirectSalesCase =
    | BeforeAppraisal of BeforeAppraisalCase
    | Appraised of AppraisedCase
    | Contracted of ContractedCase
    | ShippingInstructed of ShippingInstructedCase
    | ShippingCompleted of ShippingCompletedCase
