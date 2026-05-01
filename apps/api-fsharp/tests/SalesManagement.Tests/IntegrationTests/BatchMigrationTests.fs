module SalesManagement.Tests.IntegrationTests.BatchMigrationTests

open System
open System.IO
open Npgsql
open Xunit

let private fsharpRoot =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

let private connectionString =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

let private exec (sql: string) =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = new NpgsqlCommand(sql, conn)
    cmd.ExecuteNonQuery() |> ignore

let private queryColumnTypes (table: string) : Map<string, string> =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    use cmd =
        new NpgsqlCommand(
            "SELECT column_name, data_type
             FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = @t",
            conn
        )

    cmd.Parameters.AddWithValue("t", table) |> ignore
    use reader = cmd.ExecuteReader()
    let mutable acc = Map.empty

    while reader.Read() do
        acc <- acc.Add(reader.GetString(0), reader.GetString(1))

    acc

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``migration 007 file exists with batch table DDL`` () =
    let path = Path.Combine(fsharpRoot, "migrations", "007_create_batch_tables.sql")
    Assert.True(File.Exists path, sprintf "migration not found at %s" path)
    let sql = File.ReadAllText path
    Assert.Contains("CREATE TABLE batch_job_execution", sql)
    Assert.Contains("CREATE TABLE batch_chunk_progress", sql)
    Assert.Contains("FOREIGN KEY", sql)

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``batch_job_execution has expected columns`` () =
    let cols = queryColumnTypes "batch_job_execution"

    let expected =
        [ "job_name"
          "job_params"
          "status"
          "started_at"
          "completed_at"
          "read_count"
          "write_count"
          "skip_count"
          "error_message" ]

    for c in expected do
        Assert.True(cols.ContainsKey c, sprintf "column %s missing in batch_job_execution; got %A" c (Map.toList cols))

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``batch_chunk_progress has expected columns`` () =
    let cols = queryColumnTypes "batch_chunk_progress"

    let expected =
        [ "job_name"
          "job_params"
          "partition_id"
          "last_processed_id"
          "processed_count"
          "updated_at" ]

    for c in expected do
        Assert.True(cols.ContainsKey c, sprintf "column %s missing in batch_chunk_progress; got %A" c (Map.toList cols))

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``batch_job_execution supports insert select delete`` () =
    let jobName = sprintf "test-%s" (Guid.NewGuid().ToString("N"))
    let jobParams = "2026-04"

    try
        exec (sprintf "INSERT INTO batch_job_execution (job_name, job_params) VALUES ('%s', '%s')" jobName jobParams)

        use conn = new NpgsqlConnection(connectionString)
        conn.Open()

        use cmd =
            new NpgsqlCommand("SELECT status FROM batch_job_execution WHERE job_name = @n", conn)

        cmd.Parameters.AddWithValue("n", jobName) |> ignore
        let status = cmd.ExecuteScalar() :?> string
        Assert.Equal("RUNNING", status)
    finally
        exec (sprintf "DELETE FROM batch_job_execution WHERE job_name = '%s'" jobName)

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``batch_chunk_progress enforces foreign key to batch_job_execution`` () =
    let jobName = sprintf "test-%s" (Guid.NewGuid().ToString("N"))

    let act () =
        exec (
            sprintf
                "INSERT INTO batch_chunk_progress (job_name, job_params, last_processed_id) VALUES ('%s', 'no-parent', 0)"
                jobName
        )

    Assert.Throws<PostgresException>(act) |> ignore

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``docker-compose.yml declares localstack service with required AWS services`` () =
    let path = Path.Combine(fsharpRoot, "docker-compose.yml")
    Assert.True(File.Exists path)
    let yml = File.ReadAllText path
    Assert.Contains("localstack:", yml)
    Assert.Contains("localstack/localstack", yml)
    Assert.Contains("4566:4566", yml)
    Assert.Contains("events", yml)
    Assert.Contains("stepfunctions", yml)
    Assert.Contains("sqs", yml)
    Assert.Contains("logs", yml)

[<Fact>]
[<Trait("Category", "BatchMigration")>]
let ``localstack init script exists, is executable, and creates batch-notifications queue`` () =
    let path = Path.Combine(fsharpRoot, "localstack", "init", "setup.sh")

    Assert.True(File.Exists path, sprintf "setup.sh not found at %s" path)
    let sh = File.ReadAllText path
    Assert.Contains("awslocal sqs create-queue", sh)
    Assert.Contains("batch-notifications", sh)

    if not (OperatingSystem.IsWindows()) then
        let info = FileInfo path
        let mode = info.UnixFileMode

        Assert.True(
            (mode &&& UnixFileMode.UserExecute) <> UnixFileMode.None,
            sprintf "setup.sh must be user-executable, got %A" mode
        )
