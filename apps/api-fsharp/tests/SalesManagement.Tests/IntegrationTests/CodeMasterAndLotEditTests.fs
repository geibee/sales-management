module SalesManagement.Tests.IntegrationTests.CodeMasterAndLotEditTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

// ---- seed helpers ----

let private seedManufacturedLot (client: HttpClient) (year: int) (loc: string) (seq: int) : Task<string> = task {
    let body =
        createLotBody
            { emptyLotOverrides with
                Year = Some(JInt year)
                Location = Some(JString loc)
                Seq = Some(JInt seq) }

    let! r = postJson client "/lots" body
    Assert.Equal(HttpStatusCode.OK, r.StatusCode)
    let lotId = sprintf "%d-%s-%03d" year loc seq

    let! m = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) """{"date":"2026-01-10","version":1}"""
    Assert.Equal(HttpStatusCode.OK, m.StatusCode)
    return lotId
}

let private createCase (client: HttpClient) (lots: string list) (caseType: string) : Task<string> = task {
    let body =
        createSalesCaseBody
            { emptySalesCaseOverrides with
                Lots = Some(JArray(lots |> List.map JString))
                CaseType = Some(JString caseType) }

    let! r = postJson client "/sales-cases" body
    Assert.Equal(HttpStatusCode.OK, r.StatusCode)
    let! b = readBody r
    return (parseJson b).GetProperty("salesCaseNumber").GetString()
}

let private editLotsBody (lots: string list) (version: int) : string =
    let arr = lots |> List.map (sprintf "\"%s\"") |> String.concat ","
    sprintf """{"lots":[%s],"version":%d}""" arr version

let private availableLotNumbers (client: HttpClient) (excludeCase: string option) : Task<string list> = task {
    let path =
        match excludeCase with
        | Some c -> sprintf "/lots/available?excludeCase=%s" c
        | None -> "/lots/available"

    let! r = getReq client path
    Assert.Equal(HttpStatusCode.OK, r.StatusCode)
    let! b = readBody r

    return
        (parseJson b).GetProperty("items").EnumerateArray()
        |> Seq.map (fun e -> e.GetProperty("lotNumber").GetString())
        |> Seq.toList
}

let private containsLoc (loc: string) (items: string list) : bool =
    items |> List.exists (fun s -> s.Contains loc)

[<Collection("ApiAuthOff")>]
type CodeMasterAndLotEditTests(fixture: AuthOffFixture) =

    // ---- code masters ----

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``GET /code-masters returns seeded hierarchy``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! resp = getReq client "/code-masters"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.True(root.GetProperty("divisions").GetArrayLength() >= 2)
        // department は親 divisionCode を持つ
        let dept = root.GetProperty("departments").EnumerateArray() |> Seq.head
        Assert.True(dept.TryGetProperty("divisionCode") |> fst)
        let section = root.GetProperty("sections").EnumerateArray() |> Seq.head
        Assert.True(section.TryGetProperty("departmentCode") |> fst)
        Assert.True(root.GetProperty("processCategories").GetArrayLength() >= 1)
    }

    // ---- lot detail name resolution ----

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``lot detail resolves seeded code names``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! lotId = seedManufacturedLot client 7100 "CM" 1

        let! resp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.Equal("第一事業部", root.GetProperty("division").GetProperty("name").GetString())
        Assert.Equal("営業部", root.GetProperty("department").GetProperty("name").GetString())
        Assert.Equal("第一営業課", root.GetProperty("section").GetProperty("name").GetString())
        Assert.Equal("通常工程", root.GetProperty("processCategory").GetProperty("name").GetString())
        Assert.Equal(1, root.GetProperty("division").GetProperty("code").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``lot detail returns null name for unseeded code``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt 7101)
                    Location = Some(JString "CN")
                    Seq = Some(JInt 1)
                    DepartmentCode = Some(JInt 999) }

        let! r = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)

        let! resp = getReq client "/lots/7101-CN-1"
        let! detail = readBody resp
        let dept = (parseJson detail).GetProperty("department")
        Assert.Equal(999, dept.GetProperty("code").GetInt32())
        Assert.Equal(JsonValueKind.Null, dept.GetProperty("name").ValueKind)
    }

    // ---- available lots ----

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``available lots excludes assigned, includes own case via excludeCase``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7200 "AV" 1
        let! lotB = seedManufacturedLot client 7200 "AW" 1

        // 初期はどちらも未割当
        let! before = availableLotNumbers client None
        Assert.True(containsLoc "AV" before)
        Assert.True(containsLoc "AW" before)

        // lotA を案件に割り当てると一覧から消える
        let! caseId = createCase client [ lotA ] "direct"
        let! afterAssign = availableLotNumbers client None
        Assert.False(containsLoc "AV" afterAssign)
        Assert.True(containsLoc "AW" afterAssign)

        // excludeCase=caseId なら自案件割当の lotA は再び含まれる
        let! withExclude = availableLotNumbers client (Some caseId)
        Assert.True(containsLoc "AV" withExclude)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``available lots excludes non-manufactured lots``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        // 製造完了させない（manufacturing 状態）
        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt 7300)
                    Location = Some(JString "MF")
                    Seq = Some(JInt 1) }

        let! r = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)

        let! items = availableLotNumbers client None
        Assert.False(containsLoc "MF" items)
    }

    // ---- edit case lots ----

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots replaces direct case lots and bumps version``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7400 "EA" 1
        let! lotB = seedManufacturedLot client 7400 "EB" 1
        let! caseId = createCase client [ lotA ] "direct"

        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotB ] 1)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! b = readBody resp
        Assert.Equal(2, (parseJson b).GetProperty("version").GetInt32())

        // lotA は解放され available に戻る
        let! avail = availableLotNumbers client None
        Assert.True(containsLoc "EA" avail)
        Assert.False(containsLoc "EB" avail)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``create rejects a lot already assigned to another case``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7420 "DA" 1
        let! _ = createCase client [ lotA ] "direct"

        // 同じロットで別案件を作ろうとすると 400（二重割当を禁止）
        let body =
            createSalesCaseBody
                { emptySalesCaseOverrides with
                    Lots = Some(JArray [ JString lotA ])
                    CaseType = Some(JString "direct") }

        let! resp = postJson client "/sales-cases" body
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots keeps the case own lots (regression)``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7410 "KA" 1
        let! lotB = seedManufacturedLot client 7410 "KB" 1
        let! caseId = createCase client [ lotA; lotB ] "direct"

        // 自案件に現在割り当てられている2ロットをそのまま送る → 200 であるべき
        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotA; lotB ] 1)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots works for consignment before designation``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7500 "CA" 1
        let! lotB = seedManufacturedLot client 7500 "CB" 1
        let! caseId = createCase client [ lotA ] "consignment"

        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotB ] 1)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots rejects reservation case``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7600 "RA" 1
        let! lotB = seedManufacturedLot client 7600 "RB" 1
        let! caseId = createCase client [ lotA ] "reservation"

        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotB ] 1)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots returns 409 on stale version``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7700 "SA" 1
        let! lotB = seedManufacturedLot client 7700 "SB" 1
        let! caseId = createCase client [ lotA ] "direct"

        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotB ] 99)
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots rejects a lot assigned to another case``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7800 "OA" 1
        let! lotB = seedManufacturedLot client 7800 "OB" 1
        let! _ = createCase client [ lotA ] "direct"
        let! caseId = createCase client [ lotB ] "direct"

        // lotA は別案件に割当済み → 400
        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [ lotA ] 1)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT lots rejects empty lots``() = task {
        fixture.Reset()
        use client = fixture.NewClient()

        let! lotA = seedManufacturedLot client 7900 "PA" 1
        let! caseId = createCase client [ lotA ] "direct"

        let! resp = putJson client (sprintf "/sales-cases/%s/lots" caseId) (editLotsBody [] 1)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }
