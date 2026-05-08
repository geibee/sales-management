module SalesManagement.Tests.Support.Generators

open FsCheck.FSharp
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
