module SalesManagement.Tests.Support.StandaloneAppHost

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open SalesManagement.Hosting

/// Loopback の空きポートを 1 つ取得する。fixture 起動時にだけ使う想定。
let getFreePort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// 指定ポートの 127.0.0.1 を BaseAddress に持つ HttpClient を生成する。
let newClient (port: int) : HttpClient =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 30.0
    client

/// timeout を秒で指定できる HttpClient ファクトリ。
let newClientWithTimeout (port: int) (timeoutSec: float) : HttpClient =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds timeoutSec
    client

/// Postgres を必要としないテストで `createApp` を起動する補助型。
/// `Start` に「ポートを受け取って引数配列を返す関数」を渡すと、空きポートを割り当ててから
/// アプリを起動する。停止は `Stop` で行う。
type StandaloneApp() =
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0

    member _.Port = port
    member _.App = app

    member _.NewClient() : HttpClient = newClient port

    member _.Start(buildArgs: int -> string array) : Task =
        task {
            port <- getFreePort ()
            let args = buildArgs port
            app <- createApp args
            do! app.StartAsync()
        }
        :> Task

    member _.Stop() : Task =
        task {
            if not (isNull (box app)) then
                try
                    do! app.StopAsync()
                with _ ->
                    ()
        }
        :> Task
