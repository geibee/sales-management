module SalesManagement.Tests.Support.PbtConfig

open System
open FsCheck
open FsCheck.Xunit

/// PBT の共通実行設定 (issue #15 §3, docs/pbt-fscheck-improvement-proposal.md F6)。
///
/// FsCheck 3 は失敗時に replay seed を必ず出力する:
///   Falsifiable, after N tests (M shrinks) (seed,gamma).
///   Replay directly at failing step with (seed,gamma,size).
/// このモジュールは、その出力を環境変数 FSCHECK_REPLAY に貼り付けるだけで
/// 決定的に再実行できるようにする (Schemathesis の --seed 42 と同じ再現ポリシー)。
///
/// 使い方 (gamma は FsCheck の制約で奇数のみ。出力からの貼り付けなら常に有効):
///   FSCHECK_REPLAY="1234,5679"      # 実行全体を同じ乱数列で再生
///   FSCHECK_REPLAY="1234,5679,42"   # 失敗ステップを size 指定で直接再生
///   dotnet test --filter Category=PBT

[<Literal>]
let ReplayEnvVar = "FSCHECK_REPLAY"

let private replayFromEnv () : string option =
    match Environment.GetEnvironmentVariable ReplayEnvVar with
    | null
    | "" -> None
    | v -> Some v

/// "seed,gamma" / "(seed,gamma)" / "seed,gamma,size" を FsCheck.Replay に解析する。
/// 形式不正は黙って無視せず fail-closed でテストを落とす (seed 取り違えの防止)。
let private parseReplay (raw: string) : Replay =
    let parts =
        raw.Trim().TrimStart('(').TrimEnd(')').Split(',')
        |> Array.map (fun s -> s.Trim())

    let parseSeed (s: string) =
        match UInt64.TryParse s with
        | true, v -> v
        | false, _ -> failwithf "%s の seed 部が uint64 でない: '%s' (入力全体: '%s')" ReplayEnvVar s raw

    // Rnd は gamma 奇数などの制約を持つ。制約違反も env var 由来と分かる形で落とす
    let makeRnd (seed: string) (gamma: string) =
        try
            Rnd(parseSeed seed, parseSeed gamma)
        with :? ArgumentException as ex ->
            failwithf "%s が不正: %s (入力全体: '%s')" ReplayEnvVar ex.Message raw

    match parts with
    | [| seed; gamma |] ->
        { Rnd = makeRnd seed gamma
          Size = None }
    | [| seed; gamma; size |] ->
        { Rnd = makeRnd seed gamma
          Size = Some(Int32.Parse size) }
    | _ -> failwithf "%s の形式が不正: '%s' (期待: \"seed,gamma\" または \"seed,gamma,size\")" ReplayEnvVar raw

/// Check.One 系 PBT の標準 Config。失敗時は例外で落ち (QuickThrowOnFailure)、
/// 例外メッセージに replay seed が含まれる。FSCHECK_REPLAY が設定されていれば
/// その seed で決定的に再実行する。
let standard (maxTest: int) : Config =
    let config = Config.QuickThrowOnFailure.WithMaxTest maxTest

    match replayFromEnv () with
    | Some raw -> config.WithReplay(Some(parseReplay raw))
    | None -> config

/// [<Property>] の代替。FSCHECK_REPLAY が設定されていれば Replay として反映する
/// (PropertyAttribute.Replay は FsCheck 出力のタプル文字列をそのまま受け付ける)。
type ReplayablePropertyAttribute() as this =
    inherit PropertyAttribute()

    do
        match replayFromEnv () with
        | Some raw -> this.Replay <- raw
        | None -> ()
