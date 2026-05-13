module SalesManagement.Tests.LotStateMachinePropertyTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open SalesManagement.Domain.Types
open SalesManagement.Tests.Support.Generators

// ── コマンド型 ──

type LotCommand =
    | CompleteManufacturing of DateOnly
    | CancelManufacturingCompletion
    | InstructShipping of DateOnly
    | CompleteShipping of DateOnly
    | InstructItemConversion of ConversionDestinationInfo
    | CancelItemConversionInstruction

// ── Generator ──

let private conversionInfoGen = gen {
    return { DestinationItem = "変換先品目" }
}

let lotCommandGen: Gen<LotCommand> =
    Gen.oneof [
        Domain.dateGen |> Gen.map CompleteManufacturing
        Gen.constant CancelManufacturingCompletion
        Domain.dateGen |> Gen.map InstructShipping
        Domain.dateGen |> Gen.map CompleteShipping
        conversionInfoGen |> Gen.map InstructItemConversion
        Gen.constant CancelItemConversionInstruction
    ]

let lotCommandListGen: Gen<LotCommand list> =
    Gen.choose (0, 20) |> Gen.bind (fun n -> Gen.listOfLength n lotCommandGen)

type StateMachineArbitraries =
    static member LotCommandList() : Arbitrary<LotCommand list> = Arb.fromGen lotCommandListGen
    static member LotCommon() = Domain.Arbitraries.LotCommon()
    static member DateOnly() = Domain.Arbitraries.DateOnly()

// ── 最小 property: コマンド列が生成・shrink 可能であることの確認 ──

[<Properties(Arbitrary = [| typeof<StateMachineArbitraries> |])>]
module Tests =

    [<Property>]
    [<Trait("Category", "PBT")>]
    let ``コマンド列が生成され、長さは0〜20の範囲`` (commands: LotCommand list) =
        commands.Length >= 0 && commands.Length <= 20
