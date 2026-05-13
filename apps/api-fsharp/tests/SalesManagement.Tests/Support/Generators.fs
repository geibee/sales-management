module SalesManagement.Tests.Support.Generators

open System
open FsCheck
open FsCheck.FSharp
open SalesManagement.Domain.Types
open SalesManagement.Tests.Support.RequestBuilders

/// HTTP 用ラッパ用のドメイン generator 群。
/// S4 (stateful PBT) 投入時に `SalesCasePropertyTests.fs` 内の generator を移植する想定。
/// S1 では「最小の正常 JSON を量産する」プリミティブのみ提供する。

module Lot =

    /// 年・連番のみランダム化した正常 lot ボディを生成する `FsCheck` 用 generator。
    /// 既存 `LotPropertyTests.fs` の domain generator とは独立した HTTP 投入専用。
    let bodyGen = gen {
        let! year = Gen.choose (2020, 2030)
        let! seq = Gen.choose (1, 9999)

        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt year)
                    Seq = Some(JInt seq) }

        return body
    }

    /// 標準正常値の JSON 文字列。decisionTable 系テストでデフォルトとして使う。
    let toJson () : string = createLotBody emptyLotOverrides

module SalesCase =

    let toJson () : string =
        createSalesCaseBody emptySalesCaseOverrides

/// ドメイン型の generator 群。PBT で共通利用する。
module Domain =

    let private mustCount n =
        match Count.tryCreate n with
        | Ok c -> c
        | Error e -> failwithf "test setup: %s" e

    let private mustQuantity v =
        match Quantity.tryCreate v with
        | Ok q -> q
        | Error e -> failwithf "test setup: %s" e

    let lotDetailGen = gen {
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

    let lotCommonGen = gen {
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

    let dateGen = gen {
        let! year = Gen.choose (2020, 2030)
        let! month = Gen.choose (1, 12)
        let! day = Gen.choose (1, 28)
        return DateOnly(year, month, day)
    }

    type Arbitraries =
        static member LotCommon() : Arbitrary<LotCommon> = Arb.fromGen lotCommonGen
        static member DateOnly() : Arbitrary<DateOnly> = Arb.fromGen dateGen
