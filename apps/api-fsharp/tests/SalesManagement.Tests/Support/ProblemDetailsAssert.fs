module SalesManagement.Tests.Support.ProblemDetailsAssert

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit

/// RFC 7807 ProblemDetails の形状を検証し、root JsonElement を返す。
/// `expectedType` が空文字列のときは `type` の値はチェックしない。
let assertProblemDetails
    (expectedType: string)
    (expectedStatus: HttpStatusCode)
    (resp: HttpResponseMessage)
    : Task<JsonElement> =
    task {
        Assert.Equal(expectedStatus, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement.Clone()

        // RFC 7807 必須プロパティ
        Assert.Equal(JsonValueKind.Object, root.ValueKind)
        Assert.True(fst (root.TryGetProperty "type"), "ProblemDetails missing 'type'")
        Assert.True(fst (root.TryGetProperty "title"), "ProblemDetails missing 'title'")
        Assert.True(fst (root.TryGetProperty "status"), "ProblemDetails missing 'status'")

        Assert.Equal(int expectedStatus, root.GetProperty("status").GetInt32())

        if expectedType <> "" then
            Assert.Equal(expectedType, root.GetProperty("type").GetString())

        return root
    }

/// `validation-error` 型かつ `errors[].field` に期待フィールドが含まれることを検証。
let assertValidationError (expectedFields: string list) (resp: HttpResponseMessage) : Task<unit> = task {
    let! root = assertProblemDetails "validation-error" HttpStatusCode.BadRequest resp

    let errors =
        match root.TryGetProperty "errors" with
        | true, e when e.ValueKind = JsonValueKind.Array -> e
        | _ -> failwith "ProblemDetails has no 'errors' array"

    let actualFields =
        [ for elem in errors.EnumerateArray() do
              match elem.TryGetProperty "field" with
              | true, f when f.ValueKind = JsonValueKind.String -> yield f.GetString()
              | _ -> () ]

    for f in expectedFields do
        Assert.Contains(f, actualFields)
}

/// 指定 type の 409 Conflict を検証。
let assertConflict (expectedType: string) (resp: HttpResponseMessage) : Task<unit> = task {
    let! _ = assertProblemDetails expectedType HttpStatusCode.Conflict resp
    ()
}

/// パラメータ境界マトリクス用の二段検証。`expectedType = ""` のとき type はチェックしない。
/// S2 系テストの `case` 行と 1 対 1 で対応するため、各テストファイルで再実装しない。
let checkStatusAndType (expectedStatus: int) (expectedType: string) (resp: HttpResponseMessage) : Task<unit> = task {
    let status = enum<HttpStatusCode> expectedStatus

    if expectedType <> "" then
        let! _ = assertProblemDetails expectedType status resp
        ()
    else
        Assert.Equal(status, resp.StatusCode)
}
