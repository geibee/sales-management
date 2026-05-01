module SalesManagement.Tests.IntegrationTests.LotIdMigrationTests

open System
open System.IO
open Npgsql
open Xunit
open SalesManagement.Infrastructure

let private fsharpRoot =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

let private connectionString =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

let private execParam (sql: string) (parameters: (string * obj) list) =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    cmd.ExecuteNonQuery() |> ignore

let private queryScalarInt (sql: string) (parameters: (string * obj) list) : int64 =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)

    for (name, value) in parameters do
        cmd.Parameters.AddWithValue(name, value) |> ignore

    let result = cmd.ExecuteScalar()

    match result with
    | :? int64 as v -> v
    | :? int32 as v -> int64 v
    | null -> 0L
    | other -> Convert.ToInt64 other

let private testYear = 2099
let private testLocation = "T2"
let private testMonth = sprintf "%d-04" testYear

let private cleanup () =
    execParam
        "DELETE FROM lot_detail WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

    execParam
        "DELETE FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc"
        [ "y", box testYear; "loc", box testLocation ]

let private seedManufacturedLots (count: int) =
    cleanup ()
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use tx = conn.BeginTransaction()

    use cmd =
        new NpgsqlCommand(
            """
            INSERT INTO lot (lot_number_year, lot_number_location, lot_number_seq,
                             division_code, department_code, section_code,
                             process_category, inspection_category, manufacturing_category,
                             status, manufacturing_completed_date)
            SELECT @y, @loc, seq,
                   1, 1, 1, 1, 1, 1,
                   'manufactured', '2099-04-01'
              FROM generate_series(1, @c) AS seq
            """,
            conn,
            tx
        )

    cmd.Parameters.AddWithValue("y", testYear) |> ignore
    cmd.Parameters.AddWithValue("loc", testLocation) |> ignore
    cmd.Parameters.AddWithValue("c", count) |> ignore
    cmd.ExecuteNonQuery() |> ignore
    tx.Commit()

[<Fact>]
[<Trait("Category", "LotIdMigration")>]
let ``migration 008 adds id column to lot`` () =
    let path = Path.Combine(fsharpRoot, "migrations", "008_add_lot_id_for_batch.sql")
    Assert.True(File.Exists path, sprintf "migration not found at %s" path)
    let sql = File.ReadAllText path
    Assert.Contains("ALTER TABLE lot", sql)
    Assert.Contains("BIGSERIAL", sql)

[<Fact>]
[<Trait("Category", "LotIdMigration")>]
let ``BatchRunner project file exists with monthly-close entrypoint`` () =
    let proj = Path.Combine(fsharpRoot, "tools", "BatchRunner", "BatchRunner.fsproj")

    let prog = Path.Combine(fsharpRoot, "tools", "BatchRunner", "Program.fs")
    Assert.True(File.Exists proj, sprintf "BatchRunner.fsproj not found at %s" proj)
    Assert.True(File.Exists prog, sprintf "BatchRunner Program.fs not found at %s" prog)
    let progText = File.ReadAllText prog
    Assert.Contains("--job", progText)
    Assert.Contains("--date", progText)
    Assert.Contains("monthly-close", progText)

[<Fact>]
[<Trait("Category", "LotIdMigration")>]
let ``processInChunks transitions all manufactured lots to shipping_instructed in chunks`` () =
    try
        let total = 250
        let chunkSize = 100
        seedManufacturedLots total

        let outcome = MonthlyCloseBatch.runMonthlyClose connectionString chunkSize testMonth

        Assert.True(outcome.TotalRead >= total, sprintf "expected TotalRead ≥ %d, got %d" total outcome.TotalRead)

        Assert.True(
            outcome.TotalProcessed >= total,
            sprintf "expected TotalProcessed ≥ %d, got %d" total outcome.TotalProcessed
        )

        Assert.True(
            outcome.ChunkCount >= 3,
            sprintf "expected ≥3 chunks for %d items @ %d/chunk, got %d" total chunkSize outcome.ChunkCount
        )

        let updated =
            queryScalarInt
                "SELECT count(*) FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc AND status = 'shipping_instructed'"
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(int64 total, updated)

        let remaining =
            queryScalarInt
                "SELECT count(*) FROM lot WHERE lot_number_year = @y AND lot_number_location = @loc AND status = 'manufactured'"
                [ "y", box testYear; "loc", box testLocation ]

        Assert.Equal(0L, remaining)
    finally
        cleanup ()
