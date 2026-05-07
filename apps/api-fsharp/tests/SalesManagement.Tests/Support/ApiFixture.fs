module SalesManagement.Tests.Support.ApiFixture

open System
open System.IdentityModel.Tokens.Jwt
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Security.Claims
open System.Text
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.IdentityModel.Tokens
open Npgsql
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting

/// 認証 ON 時の HMAC-SHA256 署名鍵。テスト固定値（本番では絶対に使わない）。
let testSigningKey = "support-fixture-signing-key-please-do-not-use-in-production"

let testAudience = "sales-api"

type ApiFixtureOptions =
    { AuthEnabled: bool
      RateLimitPermits: int
      ExtraArgs: string list }

let defaultOptions: ApiFixtureOptions =
    { AuthEnabled = false
      RateLimitPermits = 100000
      ExtraArgs = [] }

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// TRUNCATE 対象テーブル。FK 依存があるため `RESTART IDENTITY CASCADE` で一括掃除する。
let private truncatableTables =
    [ "lot_detail"
      "lot"
      "appraisal"
      "lot_detail_appraisal"
      "lot_appraisal"
      "contract"
      "sales_case_lot"
      "sales_case"
      "reservation_price"
      "consignment_info"
      "consignment_result"
      "outbox_events"
      "batch_chunk_progress"
      "batch_job_execution" ]

/// Postgres + DbUp + WebApplication を一度だけ起動する xUnit fixture。
/// `ICollectionFixture` 経由で複数テストクラスから共有することで、コンテナ起動コストを償却する。
type ApiFixture(opts: ApiFixtureOptions) =
    let mutable container: PostgreSqlContainer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable connStr: string = ""

    new() = ApiFixture(defaultOptions)

    member _.Options = opts
    member _.Port = port
    member _.ConnectionString = connStr

    /// `Authentication:Enabled=false` の場合に使う、JWT を持たない素のクライアント。
    member _.NewClient() : HttpClient =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
        client.Timeout <- TimeSpan.FromSeconds 60.0
        client

    /// 指定 role を含む JWT を `Authorization: Bearer` で付与した HttpClient。
    /// `AuthEnabled=true` で起動した fixture でのみ意味がある。
    member this.NewAuthedClient(roles: string list) : HttpClient =
        let client = this.NewClient()
        let token = ApiFixture.MintToken(roles, 3600.0)
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        client

    /// 全テストデータを消す（S5 で各テストの先頭に呼ばれる想定）。
    /// FK 依存をまたぐので `CASCADE` を付ける。
    member _.Reset() : unit =
        if String.IsNullOrEmpty connStr then
            ()
        else
            use conn = new NpgsqlConnection(connStr)
            conn.Open()
            use cmd = conn.CreateCommand()

            cmd.CommandText <-
                truncatableTables
                |> String.concat ", "
                |> sprintf "TRUNCATE TABLE %s RESTART IDENTITY CASCADE"

            cmd.ExecuteNonQuery() |> ignore

    static member MintToken(roles: string list, expiresInSec: float) : string =
        let keyBytes = Encoding.UTF8.GetBytes testSigningKey
        let key = SymmetricSecurityKey keyBytes
        let creds = SigningCredentials(key, SecurityAlgorithms.HmacSha256)

        let realmAccess =
            let payload =
                roles
                |> List.map (sprintf "\"%s\"")
                |> String.concat ","
                |> sprintf """{"roles":[%s]}"""

            Claim("realm_access", payload, JsonClaimValueTypes.Json)

        let claims =
            [| Claim("sub", Guid.NewGuid().ToString())
               Claim("preferred_username", roles |> List.tryHead |> Option.defaultValue "anonymous")
               realmAccess |]

        let token =
            JwtSecurityToken(
                issuer = "support-fixture",
                audience = testAudience,
                claims = claims,
                expires = Nullable(DateTime.UtcNow.AddSeconds expiresInSec),
                signingCredentials = creds
            )

        JwtSecurityTokenHandler().WriteToken token

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

                let baseArgs =
                    [ sprintf "--Server:Port=%d" port
                      sprintf "--Database:ConnectionString=%s" connStr
                      sprintf "--RateLimit:PermitLimit=%d" opts.RateLimitPermits
                      "--RateLimit:WindowSeconds=60"
                      "--Outbox:PollIntervalMs=500"
                      "--ExternalApi:PricingUrl=http://127.0.0.1:1"
                      "--ExternalApi:TimeoutMs=500"
                      "--ExternalApi:RetryCount=0"
                      "--Logging:LogLevel:Default=Warning" ]

                let authArgs =
                    if opts.AuthEnabled then
                        [ "--Authentication:Enabled=true"
                          sprintf "--Authentication:SigningKey=%s" testSigningKey
                          sprintf "--Authentication:Audience=%s" testAudience ]
                    else
                        [ "--Authentication:Enabled=false" ]

                let args = baseArgs @ authArgs @ opts.ExtraArgs |> List.toArray

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

/// 認証 OFF の標準 fixture。多くのテストクラスはこれで足りる。
type AuthOffFixture() =
    inherit
        ApiFixture(
            { defaultOptions with
                AuthEnabled = false }
        )

/// 認証 ON の fixture。`NewAuthedClient(roles)` で JWT 付き HttpClient を取れる。
type AuthOnFixture() =
    inherit
        ApiFixture(
            { defaultOptions with
                AuthEnabled = true }
        )

[<CollectionDefinition("ApiAuthOff")>]
type AuthOffCollection() =
    interface ICollectionFixture<AuthOffFixture>

[<CollectionDefinition("ApiAuthOn")>]
type AuthOnCollection() =
    interface ICollectionFixture<AuthOnFixture>
