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

// ── モデル状態 ──

type LotModelState =
    | MManufacturing
    | MManufactured
    | MShippingInstructed
    | MShipped
    | MConversionInstructed

// ── モデル遷移 ──

type LotTransitionError = InvalidTransition of fromStatus: string * command: string

let stepModel (command: LotCommand) (state: LotModelState) : Result<LotModelState, LotTransitionError> =
    match state, command with
    | MManufacturing, CompleteManufacturing _ -> Ok MManufactured
    | MManufactured, CancelManufacturingCompletion -> Ok MManufacturing
    | MManufactured, InstructShipping _ -> Ok MShippingInstructed
    | MShippingInstructed, CompleteShipping _ -> Ok MShipped
    | MManufactured, InstructItemConversion _ -> Ok MConversionInstructed
    | MConversionInstructed, CancelItemConversionInstruction -> Ok MManufactured
    | _ -> Error(InvalidTransition(sprintf "%A" state, sprintf "%A" command))

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
