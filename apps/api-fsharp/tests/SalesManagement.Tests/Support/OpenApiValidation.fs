module SalesManagement.Tests.Support.OpenApiValidation

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
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
/// 未定義の応答・不正なスキーマ・Content-Type は失敗にする。
/// spec 外の開発用 path / 未定義 method は HTTP セマンティクステストが担う。

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

            // oneOf は厳密に1分岐、anyOf は1分岐以上に適合する必要がある。
            for branches, exact, kind in [ schema.OneOf, true, "oneOf"; schema.AnyOf, false, "anyOf" ] do
                if branches.Count > 0 then
                    let matches =
                        branches
                        |> Seq.filter (fun sub ->
                            let branchErrors = ResizeArray<string>()
                            validateSchema doc sub node path branchErrors
                            branchErrors.Count = 0)
                        |> Seq.length

                    if matches = 0 || (exact && matches <> 1) then
                        errors.Add(sprintf "%s: %s の適合分岐数が不正: %d" path kind matches)

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
                    let length = node.GetArrayLength()

                    if schema.MinItems.HasValue && length < schema.MinItems.Value then
                        errors.Add(sprintf "%s: minItems 未満" path)

                    if schema.MaxItems.HasValue && length > schema.MaxItems.Value then
                        errors.Add(sprintf "%s: maxItems 超過" path)

                    let mutable i = 0

                    for item in node.EnumerateArray() do
                        validateSchema doc schema.Items item (sprintf "%s[%d]" path i) errors
                        i <- i + 1
            | "string" ->
                if node.ValueKind <> JsonValueKind.String then
                    errors.Add(sprintf "%s: string を期待したが %A" path node.ValueKind)
                else
                    let value = node.GetString()

                    if schema.MinLength.HasValue && value.Length < schema.MinLength.Value then
                        errors.Add(sprintf "%s: minLength 未満" path)

                    if schema.MaxLength.HasValue && value.Length > schema.MaxLength.Value then
                        errors.Add(sprintf "%s: maxLength 超過" path)

                    if
                        not (String.IsNullOrEmpty schema.Pattern)
                        && not (Regex.IsMatch(value, schema.Pattern))
                    then
                        errors.Add(sprintf "%s: pattern 不一致" path)

                    if schema.Format = "date" then
                        match DateOnly.TryParseExact(value, "yyyy-MM-dd") with
                        | true, _ -> ()
                        | _ -> errors.Add(sprintf "%s: format: date に適合しない: %s" path value)
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

            if node.ValueKind = JsonValueKind.Number then
                match node.TryGetDecimal() with
                | true, value ->
                    if
                        schema.Minimum.HasValue
                        && (value < schema.Minimum.Value
                            || (schema.ExclusiveMinimum.GetValueOrDefault() && value = schema.Minimum.Value))
                    then
                        errors.Add(sprintf "%s: minimum 違反" path)

                    if
                        schema.Maximum.HasValue
                        && (value > schema.Maximum.Value
                            || (schema.ExclusiveMaximum.GetValueOrDefault() && value = schema.Maximum.Value))
                    then
                        errors.Add(sprintf "%s: maximum 違反" path)
                | _ -> errors.Add(sprintf "%s: 数値の範囲外" path)

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

let validateWithDocument
    (doc: OpenApiDocument)
    (request: HttpRequestMessage)
    (response: HttpResponseMessage)
    (ct: CancellationToken)
    : Task =
    task {
        let path = request.RequestUri.AbsolutePath

        match tryFindOperation doc request.Method path with
        | None -> () // spec 外の path / method は対象外 (dev 用エンドポイント等)
        | Some(template, operation) ->
            let statusKey = string (int response.StatusCode)

            let specResponse =
                match operation.Responses.TryGetValue statusKey with
                | true, found -> found
                | _ ->
                    match operation.Responses.TryGetValue "default" with
                    | true, found -> found
                    | _ -> failwithf "[openapi-validation] %s %s: 未記載の応答 %s" (string request.Method) template statusKey

            let specResponse = resolveResponse doc specResponse
            let! body = response.Content.ReadAsStringAsync(ct)

            if specResponse.Content.Count = 0 then
                if body <> "" then
                    failwith "[openapi-validation] body 未定義の応答に body が存在する"
            else
                let contentType =
                    match response.Content.Headers.ContentType with
                    | null -> ""
                    | header -> header.MediaType

                let media =
                    specResponse.Content
                    |> Seq.tryFind (fun (KeyValue(k, _)) ->
                        String.Equals(k, contentType, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (fun (KeyValue(_, v)) -> v)

                match media with
                | None -> failwithf "[openapi-validation] %s %s: 未定義の Content-Type '%s'" template statusKey contentType
                | Some media ->
                    if String.IsNullOrWhiteSpace body then
                        failwith "[openapi-validation] スキーマ定義があるのに body が空"

                    if contentType = "application/json" || contentType = "application/problem+json" then
                        use parsed = JsonDocument.Parse body
                        let errors = ResizeArray<string>()
                        validateSchema doc media.Schema parsed.RootElement "$" errors

                        if errors.Count > 0 then
                            failwithf "[openapi-validation] %s %s: %s" template statusKey (String.concat "; " errors)
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
            do! validateWithDocument document.Value request response cancellationToken

            if response.IsSuccessStatusCode then
                match tryFindOperation document.Value request.Method request.RequestUri.AbsolutePath with
                | Some(_, operation) -> recordOperationHit operation.OperationId
                | None -> ()

            return response
        }
