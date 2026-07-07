module SalesManagement.Tests.Support.OpenApiValidation

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.OpenApi.Any
open Microsoft.OpenApi.Models
open Microsoft.OpenApi.Readers

/// 統合テストの全レスポンスを openapi.yaml と照合する検証層 (issue #9 Tier1-5)。
///
/// ApiFixture の HttpClient に DelegatingHandler として差し込まれ、既存テストを
/// 一切書き換えずに「テストが流した実トラフィック」で契約適合を検証する。
/// Schemathesis がステートフル系エンドポイントを hooks で除外している穴を、
/// 既存統合テストのトラフィックで埋めるのが目的。
///
/// 検証ポリシー (決定的にできる範囲を段階導入する):
///   - spec に存在しない path / method / status code は対象外 (skip)。
///     status code の全数documentation 強制は Schemathesis error 昇格 (Tier2) で扱う
///   - JSON 系 (application/json / application/problem+json) のみ検証。CSV 等は対象外
///   - 適合しない場合は例外を投げ、そのリクエストを発行したテストを失敗させる

// ---------------------------------------------------------------- spec 読込

let private openapiPath =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "openapi.yaml"))

/// openapi.yaml は全テストで共有 (読み込みは一度だけ)。parse エラーは fail-closed。
let private document: Lazy<OpenApiDocument> =
    lazy
        (use stream = File.OpenRead openapiPath
         let mutable diagnostic = Unchecked.defaultof<OpenApiDiagnostic>
         let doc = OpenApiStreamReader().Read(stream, &diagnostic)

         if diagnostic.Errors.Count > 0 then
             let messages = diagnostic.Errors |> Seq.map string |> String.concat "; "

             failwithf "openapi.yaml の parse に失敗 (fail-closed): %s" messages

         doc)

/// 認可マトリクス等、spec 由来でテストケースを列挙するテストに公開する。
let specDocument () : OpenApiDocument = document.Value

// ---------------------------------------------------------------- operation カバレッジ記録

/// 統合テストが 2xx で到達した operationId の記録先。verify.sh の
/// scripts/operation-coverage-ratchet.py が「テスト未到達 operation」を
/// baseline と比較する契約カバレッジラチェットの入力になる (issue #9 Tier2-12)。
let private coverageFilePath =
    match Environment.GetEnvironmentVariable "OPERATION_COVERAGE_FILE" with
    | null
    | "" ->
        let baseDir = AppContext.BaseDirectory

        Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "coverage", "operation-coverage.json"))
    | v -> Path.GetFullPath v

let private coverageLock = obj ()
let private coveredOperations = System.Collections.Generic.HashSet<string>()

/// 新規 operationId の到達を記録し、記録ファイルを書き直す。
/// xunit コレクションは直列実行だが、念のため lock で保護する。
let private recordOperationHit (operationId: string) : unit =
    if not (String.IsNullOrEmpty operationId) then
        lock coverageLock (fun () ->
            if coveredOperations.Add operationId then
                Directory.CreateDirectory(Path.GetDirectoryName coverageFilePath) |> ignore

                let json =
                    coveredOperations
                    |> Seq.sort
                    |> Seq.map (sprintf "\"%s\"")
                    |> String.concat ", "

                File.WriteAllText(coverageFilePath, sprintf "[%s]" json))

// ---------------------------------------------------------------- $ref 解決

let private resolveSchema (doc: OpenApiDocument) (schema: OpenApiSchema) : OpenApiSchema =
    if
        not (isNull schema)
        && not (isNull schema.Reference)
        && schema.UnresolvedReference
    then
        match doc.Components.Schemas.TryGetValue schema.Reference.Id with
        | true, resolved -> resolved
        | _ -> schema
    else
        schema

let private resolveResponse (doc: OpenApiDocument) (response: OpenApiResponse) : OpenApiResponse =
    if
        not (isNull response)
        && not (isNull response.Reference)
        && response.UnresolvedReference
    then
        match doc.Components.Responses.TryGetValue response.Reference.Id with
        | true, resolved -> resolved
        | _ -> response
    else
        response

// ---------------------------------------------------------------- スキーマ照合

let private enumContains (enumValues: IOpenApiAny seq) (node: JsonElement) : bool =
    enumValues
    |> Seq.exists (fun v ->
        match v with
        | :? OpenApiString as s -> node.ValueKind = JsonValueKind.String && node.GetString() = s.Value
        | :? OpenApiInteger as i ->
            node.ValueKind = JsonValueKind.Number
            && (match node.TryGetInt32() with
                | true, n -> n = i.Value
                | _ -> false)
        | _ -> false)

/// JsonElement を OpenApiSchema に対して再帰的に照合し、違反を errors に積む。
/// OpenAPI 3.0 のうち本リポジトリの spec が使う構文 (type / required / enum /
/// nullable / allOf / oneOf / anyOf / items / format:date) を決定的に検証する。
let rec private validateSchema
    (doc: OpenApiDocument)
    (schema: OpenApiSchema)
    (node: JsonElement)
    (path: string)
    (errors: ResizeArray<string>)
    : unit =
    if isNull schema then
        ()
    else
        let schema = resolveSchema doc schema

        if node.ValueKind = JsonValueKind.Null then
            if not schema.Nullable then
                errors.Add(sprintf "%s: null は許可されていない (nullable 未指定)" path)
        else

            // allOf: 全分岐に適合すること
            for sub in schema.AllOf do
                validateSchema doc sub node path errors

            // oneOf / anyOf: 少なくとも 1 分岐に適合すること
            // (discriminator の厳密な単一適合までは要求しない)
            let branches =
                if schema.OneOf.Count > 0 then Some(schema.OneOf, "oneOf")
                elif schema.AnyOf.Count > 0 then Some(schema.AnyOf, "anyOf")
                else None

            match branches with
            | Some(subs, kind) ->
                let anyMatch =
                    subs
                    |> Seq.exists (fun sub ->
                        let branchErrors = ResizeArray<string>()
                        validateSchema doc sub node path branchErrors
                        branchErrors.Count = 0)

                if not anyMatch then
                    errors.Add(sprintf "%s: どの %s 分岐にも適合しない" path kind)
            | None -> ()

            if schema.Enum.Count > 0 && not (enumContains schema.Enum node) then
                errors.Add(sprintf "%s: enum に含まれない値: %s" path (node.GetRawText()))

            match schema.Type with
            | "object" ->
                if node.ValueKind <> JsonValueKind.Object then
                    errors.Add(sprintf "%s: object を期待したが %A" path node.ValueKind)
                else
                    for required in schema.Required do
                        match node.TryGetProperty required with
                        | true, _ -> ()
                        | _ -> errors.Add(sprintf "%s: 必須フィールド '%s' が欠落" path required)

                    for KeyValue(name, propSchema) in schema.Properties do
                        match node.TryGetProperty name with
                        | true, value -> validateSchema doc propSchema value (sprintf "%s.%s" path name) errors
                        | _ -> ()
            | "array" ->
                if node.ValueKind <> JsonValueKind.Array then
                    errors.Add(sprintf "%s: array を期待したが %A" path node.ValueKind)
                else
                    let mutable i = 0

                    for item in node.EnumerateArray() do
                        validateSchema doc schema.Items item (sprintf "%s[%d]" path i) errors
                        i <- i + 1
            | "string" ->
                if node.ValueKind <> JsonValueKind.String then
                    errors.Add(sprintf "%s: string を期待したが %A" path node.ValueKind)
                elif schema.Format = "date" then
                    match DateOnly.TryParseExact(node.GetString(), "yyyy-MM-dd") with
                    | true, _ -> ()
                    | _ -> errors.Add(sprintf "%s: format: date (yyyy-MM-dd) に適合しない: %s" path (node.GetString()))
            | "integer" ->
                let isInteger =
                    node.ValueKind = JsonValueKind.Number
                    && (match node.TryGetInt64() with
                        | true, _ -> true
                        | _ -> false)

                if not isInteger then
                    errors.Add(sprintf "%s: integer を期待したが %s" path (node.GetRawText()))
            | "number" ->
                if node.ValueKind <> JsonValueKind.Number then
                    errors.Add(sprintf "%s: number を期待したが %A" path node.ValueKind)
            | "boolean" ->
                if node.ValueKind <> JsonValueKind.True && node.ValueKind <> JsonValueKind.False then
                    errors.Add(sprintf "%s: boolean を期待したが %A" path node.ValueKind)
            | _ -> () // type 未指定 (allOf/oneOf のみのラッパ等) は上で処理済み

// ---------------------------------------------------------------- operation 解決

let private toOperationType (m: HttpMethod) : OperationType option =
    if m = HttpMethod.Get then Some OperationType.Get
    elif m = HttpMethod.Post then Some OperationType.Post
    elif m = HttpMethod.Put then Some OperationType.Put
    elif m = HttpMethod.Delete then Some OperationType.Delete
    elif m = HttpMethod.Patch then Some OperationType.Patch
    elif m = HttpMethod.Head then Some OperationType.Head
    elif m = HttpMethod.Options then Some OperationType.Options
    else None

let private templateMatches (template: string) (actual: string) : int option =
    let tSegs = template.Trim('/').Split('/')
    let aSegs = actual.Trim('/').Split('/')

    if tSegs.Length <> aSegs.Length then
        None
    else
        let mutable literals = 0
        let mutable ok = true

        for i in 0 .. tSegs.Length - 1 do
            let t = tSegs.[i]

            if t.StartsWith "{" && t.EndsWith "}" then ()
            elif t = aSegs.[i] then literals <- literals + 1
            else ok <- false

        if ok then Some literals else None

/// path templating を考慮して operation を探す。/lots/available と /lots/{id} の
/// 両方に一致するパスはリテラル一致数が多い方 (= より特殊な template) を選ぶ。
let private tryFindOperation
    (doc: OpenApiDocument)
    (method: HttpMethod)
    (path: string)
    : (string * OpenApiOperation) option =
    match toOperationType method with
    | None -> None
    | Some opType ->
        doc.Paths
        |> Seq.choose (fun (KeyValue(template, item)) ->
            templateMatches template path
            |> Option.bind (fun literals ->
                match item.Operations.TryGetValue opType with
                | true, op -> Some(literals, template, op)
                | _ -> None))
        |> Seq.sortByDescending (fun (literals, _, _) -> literals)
        |> Seq.tryHead
        |> Option.map (fun (_, template, op) -> template, op)

// ---------------------------------------------------------------- ハンドラ本体

let private validateResponse
    (request: HttpRequestMessage)
    (response: HttpResponseMessage)
    (ct: CancellationToken)
    : Task =
    task {
        let doc = document.Value
        let path = request.RequestUri.AbsolutePath

        match tryFindOperation doc request.Method path with
        | None -> () // spec 外の path / method は対象外 (dev 用エンドポイント等)
        | Some(template, operation) ->
            if int response.StatusCode >= 200 && int response.StatusCode < 300 then
                recordOperationHit operation.OperationId

            let statusKey = string (int response.StatusCode)

            match operation.Responses.TryGetValue statusKey with
            | false, _ -> () // spec 未記載の status。全数documentation の強制は Tier2 (Schemathesis error 昇格)
            | true, specResponse ->
                let specResponse = resolveResponse doc specResponse

                let contentType =
                    match response.Content.Headers.ContentType with
                    | null -> ""
                    | header ->
                        match header.MediaType with
                        | null -> ""
                        | media -> media

                let isJson =
                    contentType = "application/json" || contentType = "application/problem+json"

                if isJson && specResponse.Content.Count > 0 then
                    let mediaSchema =
                        specResponse.Content
                        |> Seq.tryFind (fun (KeyValue(k, _)) ->
                            String.Equals(k, contentType, StringComparison.OrdinalIgnoreCase))
                        |> Option.map (fun (KeyValue(_, v)) -> v.Schema)

                    match mediaSchema with
                    | None ->
                        failwithf
                            "[openapi-validation] %s %s → %s: Content-Type '%s' は spec (%s) に未定義"
                            (string request.Method)
                            path
                            statusKey
                            contentType
                            template
                    | Some schema ->
                        // ReadAsStringAsync は内容をバッファするため、後段のテスト本体からの再読込も可能
                        let! body = response.Content.ReadAsStringAsync(ct)

                        if String.IsNullOrWhiteSpace body then
                            failwithf
                                "[openapi-validation] %s %s → %s: スキーマ定義があるのにボディが空"
                                (string request.Method)
                                path
                                statusKey
                        else
                            use parsed = JsonDocument.Parse body
                            let errors = ResizeArray<string>()
                            validateSchema doc schema parsed.RootElement "$" errors

                            if errors.Count > 0 then
                                failwithf
                                    "[openapi-validation] %s %s → %s が openapi.yaml (%s) に適合しない:\n  %s\nbody: %s"
                                    (string request.Method)
                                    path
                                    statusKey
                                    template
                                    (String.concat "\n  " errors)
                                    body
    }

/// ApiFixture.NewClient に差し込む DelegatingHandler。
/// レスポンスが spec に適合しない場合は例外でそのテストを失敗させる。
type OpenApiValidationHandler(inner: HttpMessageHandler) =
    inherit DelegatingHandler(inner)

    override this.SendAsync
        (request: HttpRequestMessage, cancellationToken: CancellationToken)
        : Task<HttpResponseMessage> =
        // F# は closure 内で base を参照できないため、Task を CE の外で受ける
        let responseTask = base.SendAsync(request, cancellationToken)

        task {
            let! response = responseTask
            do! validateResponse request response cancellationToken
            return response
        }
