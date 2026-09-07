module SalesManagement.Tests.StateMachineResponseTests

open System.Net
open System.Net.Http
open Xunit
open SalesManagement.Tests.Support.HttpHelpers

[<Theory>]
[<InlineData(200)>]
[<InlineData(201)>]
[<InlineData(204)>]
[<InlineData(302)>]
[<InlineData(401)>]
[<InlineData(403)>]
[<InlineData(404)>]
[<InlineData(500)>]
[<InlineData(503)>]
[<Trait("Category", "PBT")>]
let ``XR-PBT-001 不正遷移は成功や認証失敗やサーバ障害で代替できない`` (statusCode: int) =
    use resp = new HttpResponseMessage(enum<HttpStatusCode> statusCode)
    Assert.True(contradictsExpectedFailure resp, sprintf "HTTP %d を不正遷移の拒否と誤認しました" statusCode)

[<Theory>]
[<InlineData(400)>]
[<InlineData(409)>]
[<Trait("Category", "PBT")>]
let ``XR-PBT-002 不正遷移の400と競合の409を受理する`` (statusCode: int) =
    use resp = new HttpResponseMessage(enum<HttpStatusCode> statusCode)
    Assert.False(contradictsExpectedFailure resp, sprintf "HTTP %d を成功レスポンスと誤判定しました" statusCode)
