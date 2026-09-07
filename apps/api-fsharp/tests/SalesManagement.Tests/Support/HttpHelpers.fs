module SalesManagement.Tests.Support.HttpHelpers

open System.Net.Http
open System.Net
open System.Text
open System.Text.Json
open System.Threading.Tasks

let getReq (client: HttpClient) (path: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)
    client.SendAsync req

let postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let putJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Put, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let deleteWithBody (client: HttpClient) (path: string) (body: string option) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Delete, path)

    match body with
    | Some b -> req.Content <- new StringContent(b, Encoding.UTF8, "application/json")
    | None -> ()

    client.SendAsync req

let readBody (resp: HttpResponseMessage) : Task<string> = resp.Content.ReadAsStringAsync()

let parseJson (body: string) : JsonElement =
    use doc = JsonDocument.Parse body
    doc.RootElement.Clone()

/// 存在するロット・案件への不正遷移は400または競合409で拒否される。
/// 500や認証失敗を「期待どおりの拒否」と数えない。
let contradictsExpectedFailure (resp: HttpResponseMessage) : bool =
    resp.StatusCode <> HttpStatusCode.BadRequest
    && resp.StatusCode <> HttpStatusCode.Conflict

let withHeaders (headers: (string * string) list) (req: HttpRequestMessage) : HttpRequestMessage =
    for (k, v) in headers do
        req.Headers.TryAddWithoutValidation(k, v) |> ignore

    req
