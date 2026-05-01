module SalesManagement.Tests.SalesCasePropertyTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes
open SalesManagement.Domain.SalesCaseWorkflows
open SalesManagement.Domain.ReservationCaseWorkflows
open SalesManagement.Domain.ConsignmentCaseWorkflows

let private mustCount n =
    match Count.tryCreate n with
    | Ok c -> c
    | Error e -> failwithf "test setup: %s" e

let private mustQuantity v =
    match Quantity.tryCreate v with
    | Ok q -> q
    | Error e -> failwithf "test setup: %s" e

let private mustAmount v =
    match Amount.tryCreate v with
    | Ok a -> a
    | Error e -> failwithf "test setup: %s" e

let private lotDetailGen = gen {
    return
        { ItemCategory = General
          PremiumCategory = None
          ProductCategoryCode = "A分類"
          LengthSpecLower = 100m
          ThicknessSpecLower = 10m
          ThicknessSpecUpper = 20m
          QualityGrade = "A"
          Count = mustCount 10
          Quantity = mustQuantity 5.0m
          InspectionResultCategory = None }
}

let private lotCommonGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! seq = Gen.choose (1, 9999)
    let! detail = lotDetailGen

    return
        { LotNumber =
            { Year = year
              Location = "A"
              Seq = seq }
          DivisionCode = DivisionCode 1
          DepartmentCode = DepartmentCode 1
          SectionCode = SectionCode 1
          ProcessCategory = 1
          InspectionCategory = 1
          ManufacturingCategory = 1
          Details = { Head = detail; Tail = [] } }
}

let private manufacturedLotGen = gen {
    let! common = lotCommonGen
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! day = Gen.choose (1, 28)

    return
        { Common = common
          ManufacturingCompletedDate = DateOnly(year, month, day) }
}

let private salesCaseCommonGen = gen {
    let! lot = manufacturedLotGen
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! seq = Gen.choose (1, 999)
    let! day = Gen.choose (1, 28)

    return
        { SalesCaseNumber =
            { Year = year
              Month = month
              Seq = seq }
          DivisionCode = DivisionCode 1
          SalesDate = DateOnly(year, month, day)
          Lots = { Head = Manufactured lot; Tail = [] } }
}

let private appraisalCommonGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! seq = Gen.choose (1, 999)

    let detail =
        { LotDetail =
            { ItemCategory = General
              PremiumCategory = None
              ProductCategoryCode = "A分類"
              LengthSpecLower = 100m
              ThicknessSpecLower = 10m
              ThicknessSpecUpper = 20m
              QualityGrade = "A"
              Count = mustCount 10
              Quantity = mustQuantity 5.0m
              InspectionResultCategory = None }
          BaseUnitPrice = mustAmount 1000
          PeriodAdjustmentRate = 1.0m
          CounterpartyAdjustmentRate = 1.0m
          ExceptionalPeriodAdjustmentRate = None }

    let lotAppraisal =
        { LotNumber = { Year = 2024; Location = "A"; Seq = 1 }
          ProcessingCost = None
          IndividualOrderPremium = None
          GradePremium = None
          ReservationAddon = None
          AdjustmentRate = None
          QualityAdjustmentRate = None
          ManufacturingUnitCost = None
          ExpectedSalesPeriod = None
          TargetProfitRate = None
          DetailAppraisals = { Head = detail; Tail = [] } }

    return
        { AppraisalNumber =
            { Year = year
              Month = month
              Seq = seq }
          AppraisalDate = DateOnly(year, month, 1)
          DeliveryDate = DateOnly(year, month, 15)
          SalesMarket = "国内卸売"
          BaseUnitPriceDate = "2024-01-01"
          PeriodAdjustmentRateDate = "2024-01-01"
          CounterpartyAdjustmentRateDate = "2024-01-01"
          TaxExcludedEstimatedTotal = mustAmount 500000
          LotAppraisals = { Head = lotAppraisal; Tail = [] } }
}

let private priceAppraisalGen = gen {
    let! common = appraisalCommonGen
    return Normal { Common = common }
}

let private salesContractGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! seq = Gen.choose (1, 999)

    return
        { ContractNumber =
            { Year = year
              Month = month
              Seq = seq }
          ContractDate = DateOnly(year, month, 10)
          Person = "山田太郎"
          Buyer =
            { CustomerNumber = "C001"
              AgentName = None }
          SalesInfo =
            { SalesType = 1
              Item = "試作部品A"
              DeliveryMethod = "工場渡し"
              PaymentDeferralCondition = None
              SalesMethod = 1
              Usage = None
              PaymentDeferralAmount = None }
          SalesPriceInfo =
            { TaxExcludedContractAmount = mustAmount 500000
              ConsumptionTax = mustAmount 50000
              TaxExcludedPaymentAmount = mustAmount 500000
              PaymentConsumptionTax = mustAmount 50000 }
          AppraisalNumber =
            { Year = year
              Month = month
              Seq = seq } }
}

let private shippingInstructionInfoGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! day = Gen.choose (1, 28)
    return { ShippingInstructionDate = DateOnly(year, month, day) }
}

let private shippingDateGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! day = Gen.choose (1, 28)
    return DateOnly(year, month, day)
}

let private reservationPriceCommonGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! seq = Gen.choose (1, 999)
    let! day = Gen.choose (1, 28)
    let! amount = Gen.choose (1, 1_000_000)

    return
        { AppraisalNumber =
            { Year = year
              Month = month
              Seq = seq }
          AppraisalDate = DateOnly(year, month, day)
          ReservedLotInfo = "予約ロット"
          ReservedAmount = mustAmount amount }
}

let private consignorInfoGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! day = Gen.choose (1, 28)

    return
        { ConsignorName = "委託業者A"
          ConsignorCode = "CN001"
          DesignatedDate = DateOnly(year, month, day) }
}

type SalesCaseArbitraries =
    static member SalesCaseCommon() : Arbitrary<SalesCaseCommon> = Arb.fromGen salesCaseCommonGen

    static member AppraisalCommon() : Arbitrary<AppraisalCommon> = Arb.fromGen appraisalCommonGen

    static member PriceAppraisal() : Arbitrary<PriceAppraisal> = Arb.fromGen priceAppraisalGen

    static member SalesContract() : Arbitrary<SalesContract> = Arb.fromGen salesContractGen

    static member ShippingInstructionInfo() : Arbitrary<ShippingInstructionInfo> =
        Arb.fromGen shippingInstructionInfoGen

    static member ShippingDate() : Arbitrary<DateOnly> = Arb.fromGen shippingDateGen

    static member ReservationPriceCommon() : Arbitrary<ReservationPriceCommon> = Arb.fromGen reservationPriceCommonGen

    static member ConsignorInfo() : Arbitrary<ConsignorInfo> = Arb.fromGen consignorInfoGen

[<Properties(Arbitrary = [| typeof<SalesCaseArbitraries> |])>]
module Tests =

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``査定削除後は査定前状態に戻る（往復性）`` (common: SalesCaseCommon) (appraisal: PriceAppraisal) =
        let case: BeforeAppraisalCase = { Common = common }
        let appraised = createAppraisal appraisal case
        let restored = deleteAppraisal appraised
        restored.Common = common

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``契約削除後は査定済み状態に戻る（往復性）`` (common: SalesCaseCommon) (appraisal: PriceAppraisal) (contract: SalesContract) =
        let appraised: AppraisedCase =
            { Common = common
              Appraisal = appraisal }

        let contracted = concludeContract contract appraised
        let restored = deleteContract contracted
        restored.Common = common && restored.Appraisal = appraisal

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``出庫指示取消後は契約済み状態に戻る（往復性）``
        (common: SalesCaseCommon)
        (appraisal: PriceAppraisal)
        (contract: SalesContract)
        (info: ShippingInstructionInfo)
        =
        let contracted: ContractedCase =
            { Common = common
              Appraisal = appraisal
              Contract = contract }

        let instructed = instructShipping info contracted
        let restored = cancelShippingInstruction instructed

        restored.Common = common
        && restored.Appraisal = appraisal
        && restored.Contract = contract

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``5状態を順序通り遷移できる（順序性）``
        (common: SalesCaseCommon)
        (appraisal: PriceAppraisal)
        (contract: SalesContract)
        (info: ShippingInstructionInfo)
        (date: DateOnly)
        =
        let case: BeforeAppraisalCase = { Common = common }
        let appraised = createAppraisal appraisal case
        let contracted = concludeContract contract appraised
        let instructed = instructShipping info contracted
        let completed = completeShipping date instructed

        completed.Common = common
        && completed.Appraisal = appraisal
        && completed.Contract = contract
        && completed.ShippingInstruction = info
        && completed.ShippingCompletedDate = date

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``状態遷移で SalesCaseCommon は変わらない（不変性）``
        (common: SalesCaseCommon)
        (appraisal: PriceAppraisal)
        (contract: SalesContract)
        (info: ShippingInstructionInfo)
        =
        let case: BeforeAppraisalCase = { Common = common }
        let appraised = createAppraisal appraisal case
        let contracted = concludeContract contract appraised
        let instructed = instructShipping info contracted

        appraised.Common = common
        && contracted.Common = common
        && instructed.Common = common

    let private appraisalNumberOf (appraisal: PriceAppraisal) : AppraisalNumber =
        match appraisal with
        | Normal a -> a.Common.AppraisalNumber
        | CustomerContract a -> a.Common.AppraisalNumber

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``査定更新後も AppraisalNumber は呼び出し側が渡した値と一致する（不変性）``
        (common: SalesCaseCommon)
        (appraisal: PriceAppraisal)
        (newAppraisal: PriceAppraisal)
        =
        let case: AppraisedCase =
            { Common = common
              Appraisal = appraisal }

        let updated = updateAppraisalOf newAppraisal case
        appraisalNumberOf updated.Appraisal = appraisalNumberOf newAppraisal

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``顧客契約査定には顧客契約番号と契約調整率が保持される`` (common: AppraisalCommon) (rateRaw: PositiveInt) =
        let customerContractNumber = "SA-2024-001"
        let rate = decimal rateRaw.Get / 100m

        let appraisal =
            CustomerContract
                { Common = common
                  CustomerContractNumber = customerContractNumber
                  ContractAdjustmentRate = rate }

        match appraisal with
        | CustomerContract a ->
            a.CustomerContractNumber = customerContractNumber
            && a.ContractAdjustmentRate = rate
        | Normal _ -> false

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``予約確定取消後は査定済み状態に戻る（往復性）``
        (common: SalesCaseCommon)
        (reservationCommon: ReservationPriceCommon)
        (date: DateOnly)
        (amountRaw: PositiveInt)
        =
        let amount = mustAmount amountRaw.Get
        let appraisal = Provisional { Common = reservationCommon }

        let appraised: ReservedCase =
            { Common = common
              Appraisal = appraisal }

        let determined = confirmReservation date amount appraised
        let restored = cancelReservationConfirmation determined

        restored.Common = common && restored.Appraisal = appraised.Appraisal

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``委託指定解除後は指定前に戻る（往復性）`` (common: SalesCaseCommon) (info: ConsignorInfo) =
        let before: BeforeConsignmentCase = { Common = common }
        let designated = designateConsignment info before
        let restored = cancelDesignation designated
        restored.Common = common

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``予約は4状態を順序通り遷移できる（順序性）``
        (common: SalesCaseCommon)
        (reservationCommon: ReservationPriceCommon)
        (determinedDate: DateOnly)
        (deliveryDate: DateOnly)
        (amountRaw: PositiveInt)
        =
        let amount = mustAmount amountRaw.Get
        let appraisal = Provisional { Common = reservationCommon }
        let before: BeforeReservationCase = { Common = common }
        let appraised = createReservationPrice appraisal before
        let determined = confirmReservation determinedDate amount appraised
        let delivered = deliverReservation deliveryDate determined

        appraised.Common = common
        && determined.Common = common
        && delivered.Common = common
        && delivered.DeterminedDate = determinedDate
        && delivered.DeliveryDate = deliveryDate

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``委託は3状態を順序通り遷移できる（順序性）``
        (common: SalesCaseCommon)
        (info: ConsignorInfo)
        (resultDate: DateOnly)
        (amountRaw: PositiveInt)
        =
        let amount = mustAmount amountRaw.Get
        let before: BeforeConsignmentCase = { Common = common }
        let designated = designateConsignment info before

        let result: ConsignmentResult =
            { ResultDate = resultDate
              ResultAmount = amount }

        let entered = enterConsignmentResult result designated

        designated.Common = common
        && entered.Common = common
        && entered.ConsignorInfo = info
        && entered.Result = result
