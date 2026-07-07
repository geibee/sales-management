module SalesManagement.Api.QueryGuard

open Microsoft.AspNetCore.Http
open SalesManagement.Domain.Errors

/// openapi.yaml に無いクエリパラメータを 400 で拒否するための検査。
/// 未知キーを黙って無視すると「絞り込めたつもり」で全件が返る fail-open になる
/// (Schemathesis の negative_data_rejection が検出した穴。issue #9 Tier2-15)。
let tryFindUnknownQuery (known: string list) (ctx: HttpContext) : DomainError option =
    let knownSet = Set.ofList known

    let unknown =
        ctx.Request.Query
        |> Seq.map (fun kv -> kv.Key)
        |> Seq.filter (fun key -> not (knownSet.Contains key))
        |> Seq.sort
        |> Seq.toList

    match unknown with
    | [] -> None
    | keys ->
        keys
        |> List.map (fun key ->
            { Field = key
              Message = "unknown query parameter" })
        |> ValidationFailed
        |> Some

/// 値が空のクエリパラメータ (?key=) を検出する。空値は「指定したのに効かない」
/// あいまい状態 (契約上は型/enum 違反) を作るため、未指定 (キーなし) と区別して弾く。
let private tryFindEmptyQuery (ctx: HttpContext) : DomainError option =
    let empties =
        ctx.Request.Query
        |> Seq.filter (fun kv -> System.String.IsNullOrEmpty(kv.Value.ToString()))
        |> Seq.map (fun kv -> kv.Key)
        |> Seq.sort
        |> Seq.toList

    match empties with
    | [] -> None
    | keys ->
        keys
        |> List.map (fun key ->
            { Field = key
              Message = "query parameter must not be empty" })
        |> ValidationFailed
        |> Some

/// 未知キー + 空値キーをまとめて検査する。各 GET ハンドラの入口で呼ぶ。
let tryFindInvalidQuery (known: string list) (ctx: HttpContext) : DomainError option =
    match tryFindUnknownQuery known ctx with
    | Some err -> Some err
    | None -> tryFindEmptyQuery ctx
