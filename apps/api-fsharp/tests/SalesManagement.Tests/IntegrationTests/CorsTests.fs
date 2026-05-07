module SalesManagement.Tests.IntegrationTests.CorsTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Xunit
open SalesManagement.Hosting

type CorsFixture() =
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0

    member _.Port = port

    member _.NewClient() : HttpClient =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
        client.Timeout <- TimeSpan.FromSeconds 30.0
        client

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                let listener = new TcpListener(IPAddress.Loopback, 0)
                listener.Start()
                port <- (listener.LocalEndpoint :?> IPEndPoint).Port
                listener.Stop()

                let args =
                    [| sprintf "--Server:Port=%d" port
                       "--Database:ConnectionString=Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x"
                       "--Authentication:Enabled=false"
                       "--RateLimit:PermitLimit=100000"
                       "--RateLimit:WindowSeconds=60"
                       "--Outbox:PollIntervalMs=60000"
                       "--ExternalApi:PricingUrl=http://127.0.0.1:1"
                       "--ExternalApi:TimeoutMs=500"
                       "--ExternalApi:RetryCount=0"
                       "--Cors:AllowedOrigins:0=http://localhost:5173"
                       "--Cors:AllowedOrigins:1=https://admin.example.com"
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
            }
            :> Task

[<CollectionDefinition("Cors")>]
type CorsCollection() =
    interface ICollectionFixture<CorsFixture>

let private preflight (client: HttpClient) (path: string) (origin: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Options, path)
    req.Headers.Add("Origin", origin)
    req.Headers.Add("Access-Control-Request-Method", "POST")
    req.Headers.Add("Access-Control-Request-Headers", "content-type")
    client.SendAsync req

[<Collection("Cors")>]
type CorsTests(fixture: CorsFixture) =

    [<Fact>]
    [<Trait("Category", "Cors")>]
    [<Trait("Category", "Integration")>]
    member _.``preflight from allowed origin returns CORS headers``() = task {
        use client = fixture.NewClient()
        let! resp = preflight client "/lots" "http://localhost:5173"
        // CORS preflight should be 204 (or 200)
        Assert.True(
            resp.StatusCode = HttpStatusCode.NoContent
            || resp.StatusCode = HttpStatusCode.OK,
            sprintf "expected 204/200 for allowed preflight, got %d" (int resp.StatusCode)
        )

        let allowOrigin =
            let mutable values: seq<string> = Seq.empty

            if resp.Headers.TryGetValues("Access-Control-Allow-Origin", &values) then
                String.Join(",", values)
            else
                ""

        Assert.Equal("http://localhost:5173", allowOrigin)
    }

    [<Fact>]
    [<Trait("Category", "Cors")>]
    [<Trait("Category", "Integration")>]
    member _.``preflight from disallowed origin omits Access-Control-Allow-Origin``() = task {
        use client = fixture.NewClient()
        let! resp = preflight client "/lots" "https://evil.example.com"

        let mutable values: seq<string> = Seq.empty

        let hasAllowOrigin =
            resp.Headers.TryGetValues("Access-Control-Allow-Origin", &values)

        Assert.False(hasAllowOrigin, "Access-Control-Allow-Origin should not be present for disallowed origin")
    }
