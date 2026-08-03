module SalesManagement.Tests.StateMachineResponseTests

open System.Net
open System.Net.Http
open Xunit
open SalesManagement.Tests.Support.HttpHelpers

[<Theory>]
[<InlineData(200)>]
[<InlineData(201)>]
[<InlineData(204)>]
[<Trait("Category", "PBT")>]
let ``モデルが Error を予測した場合はすべての 2xx が矛盾になる`` (statusCode: int) =
    use resp = new HttpResponseMessage(enum<HttpStatusCode> statusCode)
    Assert.True(contradictsExpectedFailure resp, sprintf "HTTP %d を成功レスポンスとして拒否できませんでした" statusCode)

[<Theory>]
[<InlineData(400)>]
[<InlineData(409)>]
[<InlineData(500)>]
[<Trait("Category", "PBT")>]
let ``モデルが Error を予測した場合は非 2xx を矛盾としない`` (statusCode: int) =
    use resp = new HttpResponseMessage(enum<HttpStatusCode> statusCode)
    Assert.False(contradictsExpectedFailure resp, sprintf "HTTP %d を成功レスポンスと誤判定しました" statusCode)
