module SalesManagement.Tests.IntegrationTests.HttpSemanticsTests

/// HTTP セマンティクスの網羅テスト (issue #15 §1)。
///
///   - 未定義メソッド → 405 + Allow ヘッダ
///   - 未知 Content-Type → 415
///   - 巨大ボディ → 413
///
/// 405 と 415 のケースは openapi.yaml から機械生成する。spec 由来の列挙なので
/// 新規エンドポイントは自動的にこのマトリクスへ取り込まれ、検査漏れも
/// 「ルート実装と spec の乖離」(実装漏れ側) も構造的に検出される。
///
/// ベースライン観測 (実装前): Giraffe の choose は未定義メソッドを 404 素通し、
/// Content-Type は無視して JSON parse 失敗の 400、巨大ボディは Kestrel 既定
/// 30MB まで素通しだった。spec 側の修正ではなくアプリ側に
/// Api/HttpSemantics.fs (spec 駆動の 405/415/413 層) を追加して解消した。
/// 405/415/413 は転送レベルのセマンティクスなので openapi.yaml の各 operation
/// には記載しない (OpenApiValidation ハンドラも spec 未記載 status は対象外)。
open System
open System.Net
open System.Net.Http
open System.Text
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.OpenApiValidation
open SalesManagement.Tests.Support.ProblemDetailsAssert

/// 405 検査に使うメソッド全集合。TRACE/CONNECT は HttpClient・Kestrel の
/// 挙動が特殊なため対象外とする。
let private probeMethods =
    [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH"; "HEAD"; "OPTIONS" ]

/// path template の {param} を実 URL 用の値に置換する。405/415 判定は
/// ルーティング前後の template 一致で決まるため、値そのものは何でもよい。
let private substituteParams (template: string) : string =
    template.Split('/')
    |> Array.map (fun seg -> if seg.StartsWith "{" then "1-1-1" else seg)
    |> String.concat "/"

/// spec の path template → 定義済みメソッド集合 (大文字)。
let private definedMethodsByPath () : (string * Set<string>) list =
    let doc = specDocument ()

    [ for KeyValue(template, item) in doc.Paths ->
          template,
          item.Operations
          |> Seq.map (fun (KeyValue(opType, _)) -> opType.ToString().ToUpperInvariant())
          |> Set.ofSeq ]

/// 未定義メソッドの全ケース: (method, template, 期待 Allow ヘッダ値)。
let private undefinedMethodCases () : obj[] seq = seq {
    for (template, defined) in definedMethodsByPath () do
        let allow = defined |> Set.toList |> List.sort |> String.concat ", "

        for method in probeMethods do
            if not (defined.Contains method) then
                yield [| box method; box template; box allow |]
}

/// requestBody を持つ全 operation: (method, template)。
let private requestBodyCases () : obj[] seq =
    let doc = specDocument ()

    seq {
        for KeyValue(template, item) in doc.Paths do
            for KeyValue(opType, op) in item.Operations do
                if not (isNull op.RequestBody) && op.RequestBody.Content.Count > 0 then
                    yield [| box (opType.ToString().ToUpperInvariant()); box template |]
    }

[<Collection("ApiAuthOff")>]
type HttpSemanticsTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    static member UndefinedMethodCases: obj[] seq = undefinedMethodCases ()
    static member RequestBodyCases: obj[] seq = requestBodyCases ()

    [<Fact>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``列挙の健全性: 未定義メソッドと requestBody 付き operation が十分に列挙される``() =
        // 列挙が壊れて 0 件になると全 Theory が silent pass するため、下限を固定する
        let undefinedCount = HttpSemanticsTests.UndefinedMethodCases |> Seq.length
        let bodyCount = HttpSemanticsTests.RequestBodyCases |> Seq.length
        Assert.True(undefinedCount >= 100, sprintf "未定義メソッドのケースが %d 件しか列挙されていない" undefinedCount)
        Assert.True(bodyCount >= 20, sprintf "requestBody 付き operation が %d 件しか列挙されていない" bodyCount)

    // ---------------------------------------------------------------- 405

    [<Theory>]
    [<MemberData(nameof HttpSemanticsTests.UndefinedMethodCases)>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``未定義メソッドは405とAllowヘッダを返す``(method: string, template: string, expectedAllow: string) = task {
        use client = fixture.NewClient()
        use req = new HttpRequestMessage(HttpMethod method, substituteParams template)
        let! resp = client.SendAsync req

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode)

        let allowValues =
            match resp.Content.Headers.TryGetValues "Allow" with
            | true, vs -> String.concat ", " vs
            | false, _ ->
                match resp.Headers.TryGetValues "Allow" with
                | true, vs -> String.concat ", " vs
                | false, _ -> ""

        Assert.Equal(expectedAllow, allowValues)

        // HEAD はボディが返らないため、problem+json の形状検証は他メソッドのみ
        if method <> "HEAD" then
            let! _ = assertProblemDetails "method-not-allowed" HttpStatusCode.MethodNotAllowed resp
            ()
    }

    // ---------------------------------------------------------------- 415

    [<Theory>]
    [<MemberData(nameof HttpSemanticsTests.RequestBodyCases)>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``未知のContent-Typeでボディを送ると415``(method: string, template: string) = task {
        use client = fixture.NewClient()
        use req = new HttpRequestMessage(HttpMethod method, substituteParams template)
        req.Content <- new StringContent("{}", Encoding.UTF8, "text/plain")
        let! resp = client.SendAsync req

        let! _ = assertProblemDetails "unsupported-media-type" HttpStatusCode.UnsupportedMediaType resp
        ()
    }

    [<Theory>]
    [<MemberData(nameof HttpSemanticsTests.RequestBodyCases)>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``Content-Type無しでボディを送ると415``(method: string, template: string) = task {
        use client = fixture.NewClient()
        use req = new HttpRequestMessage(HttpMethod method, substituteParams template)
        let content = new ByteArrayContent(Encoding.UTF8.GetBytes "{}")
        content.Headers.ContentType <- null
        req.Content <- content
        let! resp = client.SendAsync req

        let! _ = assertProblemDetails "unsupported-media-type" HttpStatusCode.UnsupportedMediaType resp
        ()
    }

    // ---------------------------------------------------------------- 413

    [<Fact>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``上限超過のContent-Length宣言ボディは413``() = task {
        use client = fixture.NewClient()
        // 既定上限 Server:MaxRequestBodyBytes = 1 MiB を超える 2 MiB
        let hugeBody = sprintf """{"padding":"%s"}""" (String('x', 2 * 1024 * 1024))
        let! resp = postJson client "/lots" hugeBody

        let! _ = assertProblemDetails "payload-too-large" HttpStatusCode.RequestEntityTooLarge resp
        ()
    }

    [<Fact>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``上限超過のchunkedボディは先読みで413``() = task {
        use client = fixture.NewClient()
        use req = new HttpRequestMessage(HttpMethod.Post, "/lots")
        let hugeBody = sprintf """{"padding":"%s"}""" (String('x', 2 * 1024 * 1024))
        req.Content <- new StringContent(hugeBody, Encoding.UTF8, "application/json")
        // Content-Length を落として chunked 転送にする。requestGuards の
        // 先読み (bufferChunkedBody) が上限+1 バイトで打ち切って 413 を返す
        req.Headers.TransferEncodingChunked <- Nullable true
        req.Content.Headers.ContentLength <- Nullable()
        let! resp = client.SendAsync req

        let! _ = assertProblemDetails "payload-too-large" HttpStatusCode.RequestEntityTooLarge resp
        ()
    }

    [<Fact>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``上限以内の正常リクエストは413にならない``() = task {
        use client = fixture.NewClient()
        // 405/415/413 層が正常系を誤って遮らないことの回帰ガード
        let! resp = getReq client "/lots"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    // ---------------------------------------------------------------- 404 素通し

    [<Fact>]
    [<Trait("Category", "HttpSemantics")>]
    [<Trait("Category", "Integration")>]
    member _.``spec外のパスは405層を素通しして404のまま``() = task {
        use client = fixture.NewClient()
        // 405 フォールバックが「未処理」を正しく返さないと、未知パスが
        // 空の 200 に化ける (choose 内の next 呼び出し事故の回帰ガード)
        let! resp1 = getReq client "/reservation-cases/2026-01-001"
        Assert.Equal(HttpStatusCode.NotFound, resp1.StatusCode)

        use req = new HttpRequestMessage(HttpMethod.Post, "/unknown/path")
        req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
        let! resp2 = client.SendAsync req
        Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode)
    }
