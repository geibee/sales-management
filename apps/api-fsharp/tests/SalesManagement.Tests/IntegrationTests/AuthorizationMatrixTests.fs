module SalesManagement.Tests.IntegrationTests.AuthorizationMatrixTests

/// 認可マトリクス (issue #9 Tier2-12)。
///
/// openapi.yaml から全 operation を機械的に列挙し、
///   - 未認証 (トークンなし) → 401
///   - 認証済みだがロールなし → 403
///   - viewer ロールでの書き込み系 (非 GET) → 403
/// を全数検証する。spec 由来の列挙なので、新規エンドポイントを追加すると
/// このマトリクスに自動的に取り込まれ、検査漏れが構造的に起きない。
open System.Net
open System.Net.Http
open System.Text
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.OpenApiValidation

/// `security: []` が明示された公開 operation (認可検査の対象外)。
let private publicOperations = Set.ofList [ "GET /health"; "GET /auth/config" ]

/// path template の {param} を実 URL 用の値に置換する。認可ゲートは
/// ルーティング前段で判定されるため、値そのものは何でもよい。
let private substituteParams (template: string) : string =
    template.Split('/')
    |> Array.map (fun seg -> if seg.StartsWith "{" then "1-1-1" else seg)
    |> String.concat "/"

let private send (client: HttpClient) (method: string) (url: string) =
    let req = new HttpRequestMessage(HttpMethod method, url)

    if method <> "GET" then
        req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")

    client.SendAsync req

let private enumerateOperations (filter: string -> bool) : obj[] seq =
    let doc = specDocument ()

    seq {
        for KeyValue(template, item) in doc.Paths do
            for KeyValue(opType, _) in item.Operations do
                let method = opType.ToString().ToUpperInvariant()
                let key = sprintf "%s %s" method template

                if not (publicOperations.Contains key) && filter method then
                    yield [| box method; box template |]
    }

[<Collection("ApiAuthOn")>]
type AuthorizationMatrixTests(fixture: AuthOnFixture) =

    /// 認可対象の全 operation。
    static member SecuredOperations: obj[] seq = enumerateOperations (fun _ -> true)

    /// 認可対象のうち書き込み系 (非 GET)。viewer ロールでは 403 になるべき operation。
    static member WriteOperations: obj[] seq = enumerateOperations (fun m -> m <> "GET")

    [<Fact>]
    [<Trait("Category", "Authorization")>]
    [<Trait("Category", "Integration")>]
    member _.``列挙の健全性: 認可対象 operation が spec の大半を占める``() =
        // 列挙が壊れて 0 件になると全 Theory が silent pass するため、下限を固定する
        let count = AuthorizationMatrixTests.SecuredOperations |> Seq.length
        Assert.True(count >= 30, sprintf "認可対象 operation が %d 件しか列挙されていない" count)

    [<Theory>]
    [<MemberData(nameof AuthorizationMatrixTests.SecuredOperations)>]
    [<Trait("Category", "Authorization")>]
    [<Trait("Category", "Integration")>]
    member _.``未認証リクエストは401``(method: string, template: string) = task {
        use client = fixture.NewClient()
        let! resp = send client method (substituteParams template)
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)
    }

    [<Theory>]
    [<MemberData(nameof AuthorizationMatrixTests.SecuredOperations)>]
    [<Trait("Category", "Authorization")>]
    [<Trait("Category", "Integration")>]
    member _.``ロールなしトークンは403``(method: string, template: string) = task {
        use client = fixture.NewAuthedClient []
        let! resp = send client method (substituteParams template)
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode)
    }

    [<Theory>]
    [<MemberData(nameof AuthorizationMatrixTests.WriteOperations)>]
    [<Trait("Category", "Authorization")>]
    [<Trait("Category", "Integration")>]
    member _.``viewerロールの書き込み系は403``(method: string, template: string) = task {
        use client = fixture.NewAuthedClient [ "viewer" ]
        let! resp = send client method (substituteParams template)
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode)
    }
