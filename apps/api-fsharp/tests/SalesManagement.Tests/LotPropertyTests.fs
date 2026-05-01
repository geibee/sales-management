module SalesManagement.Tests.LotPropertyTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open SalesManagement.Domain.Types
open SalesManagement.Domain.LotWorkflows

let private mustCount n =
    match Count.tryCreate n with
    | Ok c -> c
    | Error e -> failwithf "test setup: %s" e

let private mustQuantity v =
    match Quantity.tryCreate v with
    | Ok q -> q
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

let private dateGen = gen {
    let! year = Gen.choose (2020, 2030)
    let! month = Gen.choose (1, 12)
    let! day = Gen.choose (1, 28)
    return DateOnly(year, month, day)
}

type Arbitraries =
    static member LotCommon() : Arbitrary<LotCommon> = Arb.fromGen lotCommonGen
    static member DateOnly() : Arbitrary<DateOnly> = Arb.fromGen dateGen

[<Properties(Arbitrary = [| typeof<Arbitraries> |])>]
module Tests =

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``製造完了→取消で元の製造中ロットに戻る（往復性）`` (date: DateOnly) (common: LotCommon) =
        let lot: ManufacturingLot = { Common = common }
        let manufactured = completeManufacturing date lot
        let cancelled = cancelManufacturingCompletion manufactured
        cancelled.Common = common

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``製造中→製造完了→出荷指示→出荷完了の順序で遷移可能`` (common: LotCommon) =
        let lot: ManufacturingLot = { Common = common }
        let d1 = DateOnly(2024, 4, 1)
        let d2 = DateOnly(2024, 4, 15)
        let d3 = DateOnly(2024, 4, 20)
        let manufactured = completeManufacturing d1 lot
        let instructed = instructShipping d2 manufactured
        let shipped = completeShipping d3 instructed

        shipped.Common = common
        && shipped.ManufacturingCompletedDate = d1
        && shipped.ShippingDeadlineDate = d2
        && shipped.ShippedDate = d3

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``状態遷移でロット共通情報は変わらない（不変性）`` (common: LotCommon) =
        let lot: ManufacturingLot = { Common = common }
        let manufactured = completeManufacturing (DateOnly(2024, 1, 1)) lot
        manufactured.Common = common

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``品目変換指示取消後は製造完了状態に戻る（往復性）`` (date: DateOnly) (common: LotCommon) =
        let manufactured: ManufacturedLot =
            { Common = common
              ManufacturingCompletedDate = date }

        let info: ConversionDestinationInfo = { DestinationItem = "別品目" }
        let instructed = instructItemConversion info manufactured
        let restored = cancelItemConversionInstruction instructed

        restored.Common = common && restored.ManufacturingCompletedDate = date
