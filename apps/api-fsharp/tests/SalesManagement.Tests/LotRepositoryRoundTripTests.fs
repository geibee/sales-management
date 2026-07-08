module SalesManagement.Tests.LotRepositoryRoundTripTests

/// LotRepository の round-trip property: `insert >> loadWithVersion = Some (lot, 1)`
/// (issue #9 Tier2-14)。ドメイン型 ⇔ 行マッピングの欠落フィールド
/// (詳細行・状態別日付・変換先情報・Option 列) を決定的に検出する。
open System
open Xunit
open FsCheck
open FsCheck.FSharp
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Infrastructure
open SalesManagement.Tests.Support
open SalesManagement.Tests.Support.ApiFixture

let private mustOk =
    function
    | Ok v -> v
    | Error e -> failwithf "unexpected error: %s" e

// ── generator: 5 状態すべて + 多様な明細を持つ InventoryLot ──

let private detailGen = gen {
    let! itemCategory = Gen.elements [ General; Premium; Custom ]
    let! premium = Gen.oneof [ Gen.constant None; Gen.elements [ "A"; "特" ] |> Gen.map Some ]
    let! productCode = Gen.elements [ "P001"; "v1"; "分類A" ]
    let! lengthLower = Gen.elements [ 0.001m; 1m; 100.5m; 9999m ]
    let! thickLower = Gen.elements [ 0.5m; 1m; 10m ]
    let! thickUpper = Gen.elements [ 20m; 30.25m ]
    let! grade = Gen.elements [ "A"; "B"; "特級" ]
    let! count = Gen.choose (1, 1000)
    let! quantity = Gen.elements [ 0.001m; 1.0m; 123.456m; 5000m ]

    let! inspection = Gen.oneof [ Gen.constant None; Gen.elements [ "pass"; "合格" ] |> Gen.map Some ]

    return
        { ItemCategory = itemCategory
          PremiumCategory = premium
          ProductCategoryCode = productCode
          LengthSpecLower = lengthLower
          ThicknessSpecLower = thickLower
          ThicknessSpecUpper = thickUpper
          QualityGrade = grade
          Count = Count.tryCreate count |> mustOk
          Quantity = Quantity.tryCreate quantity |> mustOk
          InspectionResultCategory = inspection }
}

/// 各イテレーションで一意な lot 番号を払い出す (DB は共有・掃除なしで衝突しない)
let private lotSeq = ref 0

let private commonGen = gen {
    let! year = Gen.choose (2020, 2999)
    let! detailHead = detailGen
    let! detailTail = Gen.listOfLength 2 detailGen |> Gen.map (List.truncate 2)
    let! tailLen = Gen.choose (0, 2)
    let seq = System.Threading.Interlocked.Increment lotSeq

    return
        { LotNumber =
            { Year = year
              Location = "RT"
              Seq = seq }
          DivisionCode = DivisionCode 1
          DepartmentCode = DepartmentCode 10
          SectionCode = SectionCode 100
          ProcessCategory = 1
          InspectionCategory = 2
          ManufacturingCategory = 3
          Details =
            { Head = detailHead
              Tail = detailTail |> List.truncate tailLen } }
}

let private dateGen = gen {
    let! days = Gen.choose (0, 3650)
    return DateOnly(2020, 1, 1).AddDays days
}

let private lotGen = gen {
    let! common = commonGen
    let! d1 = dateGen
    let! d2 = dateGen
    let! d3 = dateGen
    let! destination = Gen.elements [ "変換先品目"; "item-B" ]
    let! pick = Gen.choose (0, 4)

    return
        match pick with
        | 0 -> Manufacturing { Common = common }
        | 1 ->
            Manufactured
                { Common = common
                  ManufacturingCompletedDate = d1 }
        | 2 ->
            ConversionInstructed
                { Common = common
                  ManufacturingCompletedDate = d1
                  DestinationInfo = { DestinationItem = destination } }
        | 3 ->
            ShippingInstructed
                { Common = common
                  ManufacturingCompletedDate = d1
                  ShippingDeadlineDate = d2 }
        | _ ->
            Shipped
                { Common = common
                  ManufacturingCompletedDate = d1
                  ShippingDeadlineDate = d2
                  ShippedDate = d3 }
}

// ── property ──

[<Collection("ApiAuthOff")>]
type LotRepositoryRoundTripTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``insert >> loadWithVersion = Some (lot, 1) (全状態 round-trip)``() =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()

        let run (lot: InventoryLot) =
            LotRepository.insert conn "round-trip-test" lot
            let lotNumber = (InventoryLot.common lot).LotNumber

            match LotRepository.loadWithVersion conn lotNumber with
            | Error e -> failwithf "load 失敗: %s" e
            | Ok None -> failwithf "insert したロットが見つからない: %A" lotNumber
            | Ok(Some(loaded, version)) ->
                if version <> 1 then
                    failwithf "初期 version は 1 のはずが %d" version

                if loaded <> lot then
                    failwithf "round-trip 不一致。\n期待: %A\n実際: %A" lot loaded

        let config = PbtConfig.standard 30
        Check.One(config, Prop.forAll (Arb.fromGen lotGen) run)
