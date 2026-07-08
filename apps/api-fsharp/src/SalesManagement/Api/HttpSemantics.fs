module SalesManagement.Api.HttpSemantics

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Giraffe
open Microsoft.OpenApi.Readers

/// openapi.yaml 由来の HTTP セマンティクス層 (issue #15 §1)。
///
/// Giraffe のルート定義は「spec に定義済みの operation の実装」だけを持ち、
/// メソッド (405)・Content-Type (415)・ボディサイズ (413) という転送レベルの
/// 検査は起動時に openapi.yaml から機械導出する。ルートと spec の二重管理に
/// なりそうな部分は、spec から path×未定義メソッドを全数生成する
/// IntegrationTests/HttpSemanticsTests.fs が乖離を検出する。
/// spec の 1 path 分。Operations は "GET" 等の大文字メソッド名 →
/// requestBody に定義された content type 一覧 (requestBody 無しは空リスト)。
type PathSpec =
    { Segments: string[]
      Operations: Map<string, string list> }

/// openapi.yaml を読み込んで PathSpec 一覧にする。起動時に一度だけ呼ぶ。
/// parse エラーは fail-fast (起動失敗) にして、壊れた spec のまま
/// 405/415 判定が silent に無効化されるのを防ぐ。
let load (openapiPath: string) : PathSpec list =
    if not (File.Exists openapiPath) then
        failwithf "openapi.yaml が見つからない: %s (405/415/413 判定に必須)" openapiPath

    use stream = File.OpenRead openapiPath
    let mutable diagnostic = Unchecked.defaultof<OpenApiDiagnostic>
    let doc = OpenApiStreamReader().Read(stream, &diagnostic)

    if diagnostic.Errors.Count > 0 then
        let messages = diagnostic.Errors |> Seq.map string |> String.concat "; "
        failwithf "openapi.yaml の parse に失敗: %s" messages

    [ for KeyValue(template, item) in doc.Paths ->
          { Segments = template.Trim('/').Split('/')
            Operations =
              item.Operations
              |> Seq.map (fun (KeyValue(opType, op)) ->
                  let contentTypes =
                      if isNull op.RequestBody then
                          []
                      else
                          op.RequestBody.Content.Keys |> List.ofSeq

                  opType.ToString().ToUpperInvariant(), contentTypes)
              |> Map.ofSeq } ]

/// path template と実パスの一致判定。一致したらリテラル一致セグメント数を返す
/// (/lots/available と /lots/{id} の両方に一致する場合、より特殊な方を選ぶため)。
let private templateMatches (spec: PathSpec) (path: string) : int option =
    let actual = path.Trim('/').Split('/')

    if spec.Segments.Length <> actual.Length then
        None
    else
        let mutable literals = 0
        let mutable ok = true

        for i in 0 .. spec.Segments.Length - 1 do
            let t = spec.Segments.[i]

            if t.StartsWith "{" && t.EndsWith "}" then
                ()
            elif String.Equals(t, actual.[i], StringComparison.Ordinal) then
                literals <- literals + 1
            else
                ok <- false

        if ok then Some literals else None

let private tryFindPath (specs: PathSpec list) (path: string) : PathSpec option =
    specs
    |> List.choose (fun spec -> templateMatches spec path |> Option.map (fun literals -> literals, spec))
    |> function
        | [] -> None
        | matches -> matches |> List.maxBy fst |> snd |> Some

let private requestMediaType (ctx: HttpContext) : string =
    match ctx.Request.ContentType with
    | null -> ""
    | ct -> ct.Split(';').[0].Trim()

let private hasRequestBody (ctx: HttpContext) : bool =
    (ctx.Request.ContentLength
     |> Option.ofNullable
     |> Option.exists (fun l -> l > 0L))
    || ctx.Request.Headers.ContainsKey "Transfer-Encoding"

/// Content-Length 宣言の無い (chunked) ボディを上限+1 バイトまで先読みする。
/// 上限以内なら巻き戻し可能な MemoryStream に差し替えて後段に渡す。
/// ルートハンドラの BindJsonAsync は例外を一律 400 に丸めるため、
/// サイズ超過の判定はルートに到達する前のこの層で確定させる必要がある。
let private bufferChunkedBody (ctx: HttpContext) (maxBodyBytes: int64) : Task<bool> = task {
    let ms = new MemoryStream()
    ctx.Response.RegisterForDispose ms
    let buffer = Array.zeroCreate 81920
    let mutable exceeded = false
    let mutable eof = false

    while not eof && not exceeded do
        let! n = ctx.Request.Body.ReadAsync(buffer, 0, buffer.Length)

        if n = 0 then
            eof <- true
        else
            ms.Write(buffer, 0, n)

            if ms.Length > maxBodyBytes then
                exceeded <- true

    if exceeded then
        return false
    else
        ms.Position <- 0L
        ctx.Request.Body <- ms
        return true
}

/// requestBody 定義に対する Content-Type の適合判定。
/// 適合しない (415 にすべき) とき Some (実際の media type) を返す。
let private tryFindUnsupportedMedia (ctx: HttpContext) (contentTypes: string list) : string option =
    if contentTypes.IsEmpty || not (hasRequestBody ctx) then
        None
    else
        let media = requestMediaType ctx

        let supported =
            contentTypes
            |> List.exists (fun c -> String.Equals(c, media, StringComparison.OrdinalIgnoreCase))

        if supported then None else Some media

/// ルーティング前段のガード。spec に定義済みの (method, path) に対してのみ
///   - requestBody 定義があるのに未知の Content-Type でボディが来た → 415
///   - ボディがサイズ上限を超える → 413 (Content-Length 宣言はヘッダで、
///     chunked は先読みで判定)
/// を返す。spec 外のリクエストは素通しし、後段 (ルート → 405/404) に委ねる。
let requestGuards (specs: PathSpec list) (maxBodyBytes: int64) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        match tryFindPath specs ctx.Request.Path.Value with
        | None -> next ctx
        | Some spec ->
            match spec.Operations.TryFind(ctx.Request.Method.ToUpperInvariant()) with
            | None -> next ctx // 未定義メソッドは methodNotAllowedFallback が 405 を返す
            | Some contentTypes ->
                let unsupportedMedia = tryFindUnsupportedMedia ctx contentTypes
                let declaredLength = ctx.Request.ContentLength |> Option.ofNullable

                let tooLargeDetail =
                    sprintf "Request body exceeds the limit of %d bytes" maxBodyBytes

                match unsupportedMedia, declaredLength with
                | Some media, _ ->
                    let detail =
                        sprintf
                            "Content-Type '%s' is not supported. Supported: %s"
                            media
                            (String.concat ", " contentTypes)

                    ProblemDetails.unsupportedMediaType detail next ctx
                | None, Some len when len > maxBodyBytes -> ProblemDetails.payloadTooLarge tooLargeDetail next ctx
                | None, Some _ -> next ctx
                | None, None when hasRequestBody ctx -> task {
                    let! withinLimit = bufferChunkedBody ctx maxBodyBytes

                    if withinLimit then
                        return! next ctx
                    else
                        return! ProblemDetails.payloadTooLarge tooLargeDetail next ctx
                  }
                | None, None -> next ctx

/// choose の最後に置くフォールバック。パスは spec に存在するがメソッドが
/// 未定義なら 405 + Allow ヘッダを返す。定義済みメソッドがルート側で
/// 一致しなかった場合 (path param の形式違い等) は skipPipeline で
/// 「未処理」を返し、後段の 404 に委ねる (choose 内で next を呼ぶと
/// 「処理済み = 空の 200」になってしまう点に注意)。
let methodNotAllowedFallback (specs: PathSpec list) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        match tryFindPath specs ctx.Request.Path.Value with
        | None -> skipPipeline
        | Some spec ->
            let method = ctx.Request.Method.ToUpperInvariant()

            if spec.Operations.ContainsKey method then
                skipPipeline
            else
                let allow =
                    spec.Operations |> Map.toList |> List.map fst |> List.sort |> String.concat ", "

                ctx.Response.Headers.["Allow"] <- StringValues allow

                let detail =
                    sprintf "Method %s is not allowed for this path. Allowed: %s" method allow

                ProblemDetails.methodNotAllowed detail next ctx
