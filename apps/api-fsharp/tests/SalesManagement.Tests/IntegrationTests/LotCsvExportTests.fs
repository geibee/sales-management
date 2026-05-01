module SalesManagement.Tests.IntegrationTests.LotCsvExportTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

type LotCsvExportFixture() =
    let mutable container: PostgreSqlContainer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable connStr: string = ""

    member _.Port = port
    member _.ConnectionString = connStr

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                container <-
                    PostgreSqlBuilder()
                        .WithImage("postgres:16-alpine")
                        .WithDatabase("sales_management")
                        .WithUsername("app")
                        .WithPassword("app")
                        .Build()

                do! container.StartAsync()
                connStr <- container.GetConnectionString()

                let upgrader =
                    DeployChanges.To
                        .PostgresqlDatabase(connStr)
                        .WithScriptsFromFileSystem(migrationsDir)
                        .LogToConsole()
                        .Build()

                let result = upgrader.PerformUpgrade()

                if not result.Successful then
                    failwithf "Migration failed: %s" (result.Error.ToString())

                port <- getFreePort ()

                let args =
                    [| sprintf "--Server:Port=%d" port
                       sprintf "--Database:ConnectionString=%s" connStr
                       "--Authentication:Enabled=false"
                       "--RateLimit:PermitLimit=100000"
                       "--RateLimit:WindowSeconds=60"
                       "--Outbox:PollIntervalMs=500"
                       "--Logging:LogLevel:Default=Warning" |]

                app <- createApp args
                do! app.StartAsync()
            }
            :> Task

        member _.DisposeAsync() : Task =
            task {
                if not (isNull (box app)) then
                    try
                        do! app.StopAsync()
                    with _ ->
                        ()

                if not (isNull (box container)) then
                    do! container.DisposeAsync()
            }
            :> Task

[<CollectionDefinition("LotCsvExport")>]
type LotCsvExportCollection() =
    interface ICollectionFixture<LotCsvExportFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let private getReq (client: HttpClient) (path: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)
    client.SendAsync req

let private createLotBody (year: int) (location: string) (seq: int) =
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

[<Collection("LotCsvExport")>]
type CsvExportTests(fixture: LotCsvExportFixture) =

    let registerCodePages =
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
        ()

    [<Fact>]
    [<Trait("Category", "LotCsvExport")>]
    [<Trait("Category", "Integration")>]
    member _.``CSV export returns Windows-31J encoded body with Japanese header``() = task {
        use client = newClient fixture.Port

        let r = Random()
        let year = 7100 + r.Next(0, 500)
        let seq = r.Next(1, 999)
        let! createResp = postJson client "/lots" (createLotBody year "X" seq)
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
        use client = newClient fixture.Port

        let r = Random()
        let year = 7600 + r.Next(0, 500)

        // Create one lot left in manufacturing
        let mfgSeq = r.Next(1, 499)
        let! resp1 = postJson client "/lots" (createLotBody year "Y" mfgSeq)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)

        // Create another lot and complete manufacturing
        let doneSeq = r.Next(500, 999)
        let! resp2 = postJson client "/lots" (createLotBody year "Y" doneSeq)
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
        use client = newClient fixture.Port

        let r = Random()
        let year = 8200 + r.Next(0, 500)
        // Insert 50 lots — keeps the test fast while still exercising bulk output.
        for i in 1..50 do
            let! resp = postJson client "/lots" (createLotBody year "Z" i)
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
