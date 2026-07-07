module SalesManagement.Tests.IntegrationTests.MigrationTests

/// マイグレーションの実行特性とスキーマスナップショットのゲート (issue #9 Tier2-13)。
///
///   - 冪等性: 全マイグレーション適用済み DB への再適用は 0 スクリプトで成功する
///   - 適用順序: 重複番号 (007/008/009) を含む適用順はファイル名昇順で固定
///     (現状はファイル名ソート依存の暗黙挙動 — ここで明示的にピンする)
///   - スキーマスナップショット: 空 DB → 全適用後の観測可能スキーマが
///     コミット済み schema-snapshot.txt と一致する (マイグレーション追加の
///     影響が PR diff で見える)
open System
open System.IO
open System.Threading.Tasks
open DbUp
open Npgsql
open Testcontainers.PostgreSql
open Xunit

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private apiRootDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

/// マイグレーションテスト専用の Postgres。ApiFixture と違いマイグレーションを
/// 適用せず、各テストが CREATE DATABASE した素の DB に対して自分で適用する。
type MigrationDbFixture() =
    let container =
        PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("migration_tests")
            .WithUsername("app")
            .WithPassword("app")
            .Build()

    member _.ConnectionString = container.GetConnectionString()

    /// 空のデータベースを新規作成し、その接続文字列を返す。
    member this.CreateFreshDatabase(name: string) : string =
        use conn = new NpgsqlConnection(this.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- sprintf "CREATE DATABASE %s" name
        cmd.ExecuteNonQuery() |> ignore

        NpgsqlConnectionStringBuilder(this.ConnectionString, Database = name).ToString()

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task = container.StartAsync()

        member _.DisposeAsync() : Task = container.DisposeAsync().AsTask()

[<CollectionDefinition("MigrationDb")>]
type MigrationDbCollection() =
    interface ICollectionFixture<MigrationDbFixture>

let private upgrade (connStr: string) =
    DeployChanges.To
        .PostgresqlDatabase(connStr)
        .WithScriptsFromFileSystem(migrationsDir)
        .LogToConsole()
        .Build()
        .PerformUpgrade()

[<Collection("MigrationDb")>]
type MigrationTests(fixture: MigrationDbFixture) =

    [<Fact>]
    [<Trait("Category", "Migration")>]
    [<Trait("Category", "Integration")>]
    member _.``マイグレーションは冪等: 2回目の適用は0スクリプトで成功する``() =
        let connStr = fixture.CreateFreshDatabase "mig_idempotency"

        let first = upgrade connStr
        Assert.True(first.Successful, sprintf "初回適用が失敗: %A" first.Error)
        Assert.NotEmpty first.Scripts

        let second = upgrade connStr
        Assert.True(second.Successful, sprintf "再適用が失敗: %A" second.Error)
        Assert.Empty second.Scripts

    [<Fact>]
    [<Trait("Category", "Migration")>]
    [<Trait("Category", "Integration")>]
    member _.``適用順序はファイル名昇順で固定 (重複番号 007/008/009 を含む)``() =
        let connStr = fixture.CreateFreshDatabase "mig_ordering"
        let result = upgrade connStr
        Assert.True(result.Successful, sprintf "適用が失敗: %A" result.Error)

        let expected =
            Directory.GetFiles(migrationsDir, "*.sql")
            |> Array.map Path.GetFileName
            |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))

        use conn = new NpgsqlConnection(connStr)
        conn.Open()
        use cmd = conn.CreateCommand()
        // ファイル名は FileSystemScriptProvider がフルパスで journal に記録する
        cmd.CommandText <- "SELECT scriptname FROM schemaversions ORDER BY schemaversionsid"
        use reader = cmd.ExecuteReader()

        let applied =
            [| while reader.Read() do
                   yield Path.GetFileName(reader.GetString 0) |]

        Assert.Equal<string[]>(expected, applied)

    [<Fact>]
    [<Trait("Category", "Migration")>]
    [<Trait("Category", "Integration")>]
    member _.``空DBへの全適用がコミット済みスキーマスナップショットと一致する``() =
        let connStr = fixture.CreateFreshDatabase "mig_snapshot"
        let result = upgrade connStr
        Assert.True(result.Successful, sprintf "適用が失敗: %A" result.Error)

        let sql =
            File.ReadAllText(Path.Combine(apiRootDir, "scripts", "schema-snapshot.sql"))

        use conn = new NpgsqlConnection(connStr)
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- sql
        use reader = cmd.ExecuteReader()

        let actual =
            [| while reader.Read() do
                   yield reader.GetString 0 |]
            |> String.concat "\n"

        let snapshotPath = Path.Combine(apiRootDir, "schema-snapshot.txt")

        if Environment.GetEnvironmentVariable "UPDATE_SCHEMA_SNAPSHOT" = "1" then
            File.WriteAllText(snapshotPath, actual + "\n")
        else
            Assert.True(File.Exists snapshotPath, sprintf "schema-snapshot.txt がありません: %s" snapshotPath)

            let expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n").TrimEnd('\n')

            if expected <> actual then
                // bootstrap / 差分確認をログから行えるよう実測値を全文出力する
                failwithf
                    "スキーマスナップショット不一致。マイグレーションの影響を確認し、意図した変更なら\nUPDATE_SCHEMA_SNAPSHOT=1 でローカル再生成して schema-snapshot.txt をコミットしてください。\n--- actual ---\n%s"
                    actual
