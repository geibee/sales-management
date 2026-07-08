module SalesManagement.Tests.IntegrationTests.LotCsvExportTests

open System
open System.Net
open System.Text
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private lotBody (year: int) (location: string) (seq: int) =
    sprintf
        """{
            "lotNumber": {"year": %d, "location": "%s", "seq": %d},
            "divisionCode": 1, "departmentCode": 1, "sectionCode": 1,
            "processCategory": 1, "inspectionCategory": 1, "manufacturingCategory": 1,
            "details": [
                {"itemCategory": "general", "premiumCategory": "", "productCategoryCode": "v",
                 "lengthSpecLower": 1.0, "thicknessSpecLower": 1.0, "thicknessSpecUpper": 2.0,
                 "qualityGrade": "A", "count": 1, "quantity": 1.0, "inspectionResultCategory": ""}
            ]
        }"""
        year
        location
        seq

[<Collection("ApiAuthOff")>]
type CsvExportTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    let registerCodePages =
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        ()

    [<Fact>]
    [<Trait("Category", "LotCsvExport")>]
    [<Trait("Category", "Integration")>]
    member _.``CSV export returns Windows-31J encoded body with Japanese header``() = task {
        use client = fixture.NewClient()

        let r = Random()
        let year = 7100 + r.Next(0, 500)
        let seq = r.Next(1, 999)
        let! createResp = postJson client "/lots" (lotBody year "X" seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! resp = getReq client "/lots/export?format=csv"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        // Content-Type header
        let contentType = resp.Content.Headers.ContentType
        Assert.NotNull(contentType)
        Assert.Equal("text/csv", contentType.MediaType)
        Assert.Equal("windows-31j", contentType.CharSet)

        // Content-Disposition header
        let cd = resp.Content.Headers.ContentDisposition
        Assert.NotNull(cd)
        Assert.Equal("attachment", cd.DispositionType)
        let fn = if isNull cd.FileName then "" else cd.FileName.Trim('"')
        Assert.StartsWith("lots_", fn)
        Assert.EndsWith(".csv", fn)

        // Body decoded with Windows-31J should contain Japanese header
        let! bytes = resp.Content.ReadAsByteArrayAsync()
        let encoding = Encoding.GetEncoding(932)
        let decoded = encoding.GetString(bytes)
        let firstLine = decoded.Split('\n').[0].TrimEnd('\r')
        Assert.Equal("\"ロット番号\",\"事業部\",\"状態\",\"製造完了日\"", firstLine)

        // Lot we created should appear
        let lotId = sprintf "%d-X-%03d" year seq
        Assert.Contains(lotId, decoded)
    }

    [<Fact>]
    [<Trait("Category", "LotCsvExport")>]
    [<Trait("Category", "Integration")>]
    member _.``CSV export filtered by status returns only matching rows``() = task {
        use client = fixture.NewClient()

        let r = Random()
        let year = 7600 + r.Next(0, 500)

        // Create one lot left in manufacturing
        let mfgSeq = r.Next(1, 499)
        let! resp1 = postJson client "/lots" (lotBody year "Y" mfgSeq)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)

        // Create another lot and complete manufacturing
        let doneSeq = r.Next(500, 999)
        let! resp2 = postJson client "/lots" (lotBody year "Y" doneSeq)
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode)
        let lotIdDone = sprintf "%d-Y-%03d" year doneSeq

        let! mutateResp =
            postJson
                client
                (sprintf "/lots/%s/complete-manufacturing" lotIdDone)
                """{"date":"2026-04-25","version":1}"""

        Assert.Equal(HttpStatusCode.OK, mutateResp.StatusCode)

        // Filter export to manufactured only
        let! exportResp = getReq client "/lots/export?format=csv&status=manufactured"
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode)

        let! bytes = exportResp.Content.ReadAsByteArrayAsync()
        let encoding = Encoding.GetEncoding(932)
        let body = encoding.GetString(bytes)

        let lotIdMfg = sprintf "%d-Y-%03d" year mfgSeq
        Assert.Contains(lotIdDone, body)
        Assert.DoesNotContain(lotIdMfg, body)
        Assert.Contains("\"manufactured\"", body)
        Assert.DoesNotContain("\"manufacturing\"", body)
    }

    [<Fact>]
    [<Trait("Category", "LotCsvExport")>]
    [<Trait("Category", "Integration")>]
    member _.``CSV export streams many rows without timeout``() = task {
        use client = fixture.NewClient()

        let r = Random()
        let year = 8200 + r.Next(0, 500)
        // Insert 50 lots — keeps the test fast while still exercising bulk output.
        for i in 1..50 do
            let! resp = postJson client "/lots" (lotBody year "Z" i)
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        let sw = System.Diagnostics.Stopwatch.StartNew()
        let! resp = getReq client "/lots/export?format=csv"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! bytes = resp.Content.ReadAsByteArrayAsync()
        sw.Stop()

        Assert.True(bytes.Length > 0)
        // Header + 50 data rows means at least 50 newline characters in the output.
        let encoding = Encoding.GetEncoding(932)
        let decoded = encoding.GetString(bytes)

        let lineCount =
            decoded.Split('\n')
            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
            |> Array.length

        Assert.True(lineCount >= 51, sprintf "expected >=51 lines, got %d" lineCount)
        Assert.True(sw.ElapsedMilliseconds < 10000L, sprintf "export took %dms" sw.ElapsedMilliseconds)
    }

    /// CSV インジェクション検査 (issue #15 §2)。API のバリデーションでは投入できない
    /// 悪意ある値 (Excel が数式として解釈する = + - @ TAB CR 始まり) を SQL で直接
    /// seeding し、出力セルが "'" 前置で無害化されることを全数 assert する。
    [<Fact>]
    [<Trait("Category", "LotCsvExport")>]
    [<Trait("Category", "Integration")>]
    member _.``CSV export neutralizes formula injection in cell values``() = task {
        // (seq, 悪意ある status 値)。status は DB 制約が TEXT のみなので
        // 侵害・移行バグ等で任意文字列が混入し得る列として代表させる
        let maliciousStatuses =
            [ 1, "=CMD('/C calc')!A0"
              2, "+2+5+cmd|' /C calc'!A0"
              3, "-2+3+cmd|' /C calc'!A0"
              4, "@SUM(1+9)*cmd|' /C calc'!A0"
              5, "\tCMD"
              6, "\rCMD"
              7, "=HYPERLINK(\"http://evil\",\"click\")" ]

        do! task {
            use conn = new NpgsqlConnection(fixture.ConnectionString)
            do! conn.OpenAsync()

            for (seq, status) in maliciousStatuses do
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """INSERT INTO lot (lot_number_year, lot_number_location, lot_number_seq,
                                        division_code, department_code, section_code,
                                        process_category, inspection_category, manufacturing_category, status)
                       VALUES (9900, 'V', @seq, 1, 1, 1, 1, 1, 1, @status)"""

                cmd.Parameters.AddWithValue("seq", seq) |> ignore
                cmd.Parameters.AddWithValue("status", status) |> ignore
                let! _ = cmd.ExecuteNonQueryAsync()
                ()
        }

        use client = fixture.NewClient()
        let! resp = getReq client "/lots/export?format=csv"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        let! bytes = resp.Content.ReadAsByteArrayAsync()
        let decoded = Encoding.GetEncoding(932).GetString(bytes)
        let lines = decoded.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))

        for (seq, status) in maliciousStatuses do
            let lotId = sprintf "9900-V-%03d" seq

            let line =
                lines
                |> Array.tryFind (fun l -> l.Contains lotId)
                |> Option.defaultWith (fun () -> failwithf "lot %s の行が CSV に見つからない" lotId)

            // 数式トリガ文字は "'" 前置 + 全体は二重引用符囲み。内部の '"' は '""' に倍化される
            let expectedCell = sprintf "\"'%s\"" (status.Replace("\"", "\"\""))
            Assert.Contains(expectedCell, line)

            // 生の (無害化前の) セルが二重引用符囲みで露出していないこと
            let rawCell = sprintf "\"%s\"" (status.Replace("\"", "\"\""))
            Assert.DoesNotContain(rawCell, line)
    }
