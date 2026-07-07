module SalesManagement.Tests.SmartConstructorTests

/// smart constructor の境界値テーブルテスト + 仕様述語との一致 PBT
/// (issue #9 Tier2-14 / docs/pbt-fscheck-improvement-proposal.md F3)。
/// 「tryCreate の成功/失敗が仕様の述語と一致する」ことを唯一の oracle にする。
open System
open Xunit
open FsCheck
open FsCheck.FSharp
open SalesManagement.Domain.Types
open SalesManagement.Domain.SmartConstructors

let private isOk =
    function
    | Ok _ -> true
    | Error _ -> false

let private pbtConfig = Config.QuickThrowOnFailure.WithMaxTest(200)

/// 境界周辺 + 極値を厚めに引く int generator (デフォルト Arb の分布に依存しない)
let private intGen =
    Gen.frequency
        [ 4, Gen.choose (-1000, 1000)
          1,
          Gen.elements
              [ Int32.MinValue
                Int32.MinValue + 1
                -1
                0
                1
                Int32.MaxValue - 1
                Int32.MaxValue ] ]

let private stringGen =
    Gen.oneof
        [ Gen.elements [ null; ""; " "; "　"; "\t"; "\n"; " \u3000 "; "a"; " a "; "𩸽"; "日本語"; "x-1" ]
          Gen.elements [ 'a' .. 'z' ]
          |> Gen.nonEmptyListOf
          |> Gen.map (List.toArray >> String) ]

let private decimalGen =
    Gen.frequency
        [ 4, Gen.choose (-1000000, 1000000) |> Gen.map (fun n -> decimal n / 1000m)
          1, Gen.elements [ 0m; 0.001m; 0.0009m; -0.001m; 1m; 999999999m ] ]

// ── PositiveInt ──

[<Theory>]
[<InlineData(1, true)>]
[<InlineData(0, false)>]
[<InlineData(-1, false)>]
[<InlineData(Int32.MaxValue, true)>]
[<InlineData(Int32.MinValue, false)>]
[<Trait("Category", "SmartConstructor")>]
let ``PositiveInt: 境界値`` (value: int) (expected: bool) =
    Assert.Equal(expected, isOk (PositiveInt.tryCreate value))

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``PositiveInt: 成功 ⇔ value > 0 (PBT)`` () =
    Check.One(pbtConfig, Prop.forAll (Arb.fromGen intGen) (fun n -> isOk (PositiveInt.tryCreate n) = (n > 0)))

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``PositiveInt: value は入力を保存する`` () =
    match PositiveInt.tryCreate 42 with
    | Ok p -> Assert.Equal(42, PositiveInt.value p)
    | Error e -> failwithf "unexpected error: %s" e

// ── NonEmptyString ──

[<Theory>]
[<InlineData("a", true)>]
[<InlineData("", false)>]
[<InlineData(" ", false)>]
[<InlineData("　", false)>] // 全角空白 (U+3000)
[<InlineData("\t\n", false)>]
[<InlineData(" a ", true)>]
[<InlineData("𩸽", true)>] // サロゲートペア
[<InlineData(null, false)>]
[<Trait("Category", "SmartConstructor")>]
let ``NonEmptyString: 境界値`` (value: string) (expected: bool) =
    Assert.Equal(expected, isOk (NonEmptyString.tryCreate value))

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``NonEmptyString: 成功 ⇔ not IsNullOrWhiteSpace (PBT)`` () =
    Check.One(
        pbtConfig,
        Prop.forAll (Arb.fromGen stringGen) (fun s ->
            isOk (NonEmptyString.tryCreate s) = not (String.IsNullOrWhiteSpace s))
    )

// ── Amount ──

[<Theory>]
[<InlineData(0, true)>]
[<InlineData(1, true)>]
[<InlineData(-1, false)>]
[<InlineData(Int32.MaxValue, true)>]
[<InlineData(Int32.MinValue, false)>]
[<Trait("Category", "SmartConstructor")>]
let ``Amount: 境界値`` (value: int) (expected: bool) =
    Assert.Equal(expected, isOk (Amount.tryCreate value))

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``Amount: 成功 ⇔ value >= 0 (PBT)`` () =
    Check.One(pbtConfig, Prop.forAll (Arb.fromGen intGen) (fun n -> isOk (Amount.tryCreate n) = (n >= 0)))

// ── Count ──

[<Theory>]
[<InlineData(1, true)>]
[<InlineData(0, false)>]
[<InlineData(-1, false)>]
[<InlineData(Int32.MaxValue, true)>]
[<Trait("Category", "SmartConstructor")>]
let ``Count: 境界値`` (value: int) (expected: bool) =
    Assert.Equal(expected, isOk (Count.tryCreate value))

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``Count: 成功 ⇔ value >= 1 (PBT)`` () =
    Check.One(pbtConfig, Prop.forAll (Arb.fromGen intGen) (fun n -> isOk (Count.tryCreate n) = (n >= 1)))

// ── Quantity ──

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``Quantity: 境界値テーブル`` () =
    let cases =
        [ 0.001m, true // 下限ちょうど
          0.0009m, false // 下限のすぐ下
          0m, false
          -1m, false
          1m, true
          9999999.999m, true ]

    for value, expected in cases do
        let actual = isOk (Quantity.tryCreate value)
        Assert.True((expected = actual), sprintf "Quantity.tryCreate %M の期待は %b" value expected)

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``Quantity: 成功 ⇔ value >= 0.001 (PBT)`` () =
    Check.One(pbtConfig, Prop.forAll (Arb.fromGen decimalGen) (fun d -> isOk (Quantity.tryCreate d) = (d >= 0.001m)))

// ── LotNumber ──

[<Theory>]
[<InlineData("2026-A-001")>]
[<InlineData("1-x-1")>]
[<InlineData("9999-倉庫-999")>]
[<Trait("Category", "SmartConstructor")>]
let ``LotNumber: 正準形式は parse できる`` (s: string) =
    Assert.True((LotNumber.tryParse s).IsSome, s)

[<Theory>]
[<InlineData("")>]
[<InlineData("a-b")>]
[<InlineData("1-2-3-4")>]
[<InlineData("x-y-z")>]
[<InlineData("2026--001")>]
[<InlineData("2026-A-xyz")>]
[<Trait("Category", "SmartConstructor")>]
let ``LotNumber: 不正形式は parse できない`` (s: string) =
    Assert.True((LotNumber.tryParse s).IsNone, s)

[<Fact>]
[<Trait("Category", "SmartConstructor")>]
let ``LotNumber: toString >> tryParse = Some (round-trip PBT)`` () =
    let lotNumberGen = gen {
        let! year = Gen.choose (1, 9999)
        let! seq = Gen.choose (1, 9999)
        let! location = Gen.elements [ "A"; "F12A"; "倉庫1"; "x_y" ]

        return
            { Year = year
              Location = location
              Seq = seq }
    }

    Check.One(
        pbtConfig,
        Prop.forAll (Arb.fromGen lotNumberGen) (fun n -> LotNumber.tryParse (LotNumber.toString n) = Some n)
    )
