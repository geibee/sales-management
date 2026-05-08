module SalesManagement.Tests.IntegrationTests.BatchInputParserTests

open System
open System.IO
open System.Text
open Npgsql
open Xunit
open SalesManagement.Infrastructure
open SalesManagement.Tests.Support.BatchFixture

let private writeTempCsv (lines: string seq) (encoding: Encoding) : string =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "import-%s.csv" (Guid.NewGuid().ToString("N")))

    File.WriteAllLines(path, lines, encoding)
    path

let private header = "ロット番号年度,ロット番号保管場所,ロット番号連番,事業部コード,部門コード,担当課コード,工程区分,検査区分,製造区分"

[<Fact>]
[<Trait("Category", "BatchInputParser")>]
let ``parseLine accepts a valid row and rejects invalid seq`` () =
    match CsvImportBatch.parseLine 1L "2099,Z9,1,1,1,1,1,1,1" with
    | Ok row ->
        Assert.Equal(2099, row.LotNumberYear)
        Assert.Equal("Z9", row.LotNumberLocation)
        Assert.Equal(1, row.LotNumberSeq)
    | Error e -> Assert.Fail(sprintf "expected Ok, got Error %s" e)

    match CsvImportBatch.parseLine 4L "2099,Z9,invalid,1,1,1,1,1,1" with
    | Ok _ -> Assert.Fail "expected Error"
    | Error msg -> Assert.Contains("lot_number_seq", msg)

[<Fact>]
[<Trait("Category", "BatchInputParser")>]
let ``runImportLotsManaged inserts valid rows and skips invalid ones`` () =
    let testYear = 2099
    let testLocation = "Z1"
    let jobName = sprintf "import-lots-%s" (Guid.NewGuid().ToString("N"))

    let csvLines =
        [ header
          sprintf "%d,%s,1,1,1,1,1,1,1" testYear testLocation
          sprintf "%d,%s,2,1,1,1,1,1,1" testYear testLocation
          sprintf "%d,%s,3,1,1,1,1,1,1" testYear testLocation
          sprintf "%d,%s,invalid,1,1,1,1,1,1" testYear testLocation
          sprintf "%d,%s,5,1,1,1,1,1,1" testYear testLocation ]

    let path = writeTempCsv csvLines Encoding.UTF8

    try
        cleanupLots testYear testLocation

        let outcome =
            CsvImportBatch.runImportLotsManaged connectionString 100 jobName path Encoding.UTF8

        match outcome with
        | CsvImportBatch.Completed o ->
            Assert.Equal(5, o.TotalRead)
            Assert.Equal(4, o.TotalWritten)
            Assert.Equal(1, o.TotalSkipped)
        | other -> Assert.Fail(sprintf "expected Completed, got %A" other)

        // batch_job_execution に結果が記録されること
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()

        use cmd =
            new NpgsqlCommand(
                "SELECT status, read_count, write_count, skip_count FROM batch_job_execution WHERE job_name = @n AND job_params = @p",
                conn
            )

        cmd.Parameters.AddWithValue("n", jobName) |> ignore
        cmd.Parameters.AddWithValue("p", path) |> ignore
        use rd = cmd.ExecuteReader()
        Assert.True(rd.Read(), "batch_job_execution row should exist")
        Assert.Equal("COMPLETED", rd.GetString(0))
        Assert.Equal(5, rd.GetInt32(1))
        Assert.Equal(4, rd.GetInt32(2))
        Assert.Equal(1, rd.GetInt32(3))
        rd.Close()

        // 正常行 (連番 1, 2, 3, 5) が manufacturing 状態で登録されていること
        use cmd2 =
            new NpgsqlCommand(
                "SELECT lot_number_seq, status FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc ORDER BY lot_number_seq",
                conn
            )

        cmd2.Parameters.AddWithValue("y", testYear) |> ignore
        cmd2.Parameters.AddWithValue("loc", testLocation) |> ignore
        use rd2 = cmd2.ExecuteReader()
        let seqs = ResizeArray<int>()

        while rd2.Read() do
            seqs.Add(rd2.GetInt32(0))
            Assert.Equal("manufacturing", rd2.GetString(1))

        Assert.Equal<int list>([ 1; 2; 3; 5 ], List.ofSeq seqs)
    finally
        cleanupLots testYear testLocation
        cleanupJob jobName path

        if File.Exists path then
            File.Delete path

[<Fact>]
[<Trait("Category", "BatchInputParser")>]
let ``runImportLots reads Windows-31J encoded CSV correctly`` () =
    let testYear = 2099
    let testLocation = "Z2"
    let encoding = CsvImportBatch.resolveEncoding "windows-31j"

    let csvLines =
        [ header
          sprintf "%d,%s,1,1,1,1,1,1,1" testYear testLocation
          sprintf "%d,%s,2,1,1,1,1,1,1" testYear testLocation ]

    let path = writeTempCsv csvLines encoding

    try
        cleanupLots testYear testLocation

        let outcome = CsvImportBatch.runImportLots connectionString 100 path encoding

        Assert.Equal(2, outcome.TotalRead)
        Assert.Equal(2, outcome.TotalWritten)
        Assert.Equal(0, outcome.TotalSkipped)

        let count =
            queryScalarInt
                "SELECT count(*) FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(2L, count)
    finally
        cleanupLots testYear testLocation

        if File.Exists path then
            File.Delete path

[<Fact>]
[<Trait("Category", "BatchInputParser")>]
let ``runImportLots chunks large CSV (1万 rows) and writes all`` () =
    let testYear = 2099
    let testLocation = "Z3"
    let total = 10000
    let chunkSize = 1000

    let csvLines = seq {
        yield header

        for i in 1..total do
            yield sprintf "%d,%s,%d,1,1,1,1,1,1" testYear testLocation i
    }

    let path = writeTempCsv csvLines Encoding.UTF8

    try
        cleanupLots testYear testLocation

        let outcome =
            CsvImportBatch.runImportLots connectionString chunkSize path Encoding.UTF8

        Assert.Equal(total, outcome.TotalRead)
        Assert.Equal(total, outcome.TotalWritten)
        Assert.Equal(0, outcome.TotalSkipped)

        Assert.True(
            outcome.ChunkCount >= total / chunkSize,
            sprintf "expected ≥%d chunks, got %d" (total / chunkSize) outcome.ChunkCount
        )

        let count =
            queryScalarInt
                "SELECT count(*) FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(int64 total, count)
    finally
        cleanupLots testYear testLocation

        if File.Exists path then
            File.Delete path
