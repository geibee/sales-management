module SalesManagement.Tests.LotPropertyTests

open System
open Xunit
open FsCheck.Xunit
open SalesManagement.Domain.Types
open SalesManagement.Domain.LotWorkflows
open SalesManagement.Tests.Support.Generators

[<Properties(Arbitrary = [| typeof<Domain.Arbitraries> |])>]
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
