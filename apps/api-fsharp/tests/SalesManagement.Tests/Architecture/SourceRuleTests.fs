module SalesManagement.Tests.Architecture.SourceRuleTests

// ソースコードの禁止パターン検査 (issue #9 Tier2-18)。
//
// TypeScript 側の禁止パターンは ast-grep (`.ast-grep/rules/`) が担うが、
// ast-grep に F# の文法サポートが無いため、F# 側は本テストが同じ役割を担う
// (Architecture カテゴリとして verify のマージゲートで実行される)。
// ルールを追加・緩和するときは AGENTS.md の「静的検査可能なルールは
// linter か ast-grep で記述する」の方針に従い、ここに理由を書き残すこと。

open System.IO
open System.Text.RegularExpressions
open Xunit

let private srcRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "src", "SalesManagement"))

let private sourceFiles (subdir: string) : string list =
    let dir = Path.Combine(srcRoot, subdir)
    Assert.True(Directory.Exists dir, sprintf "ソースディレクトリがありません (fail-closed): %s" dir)
    Directory.GetFiles(dir, "*.fs", SearchOption.AllDirectories) |> Array.toList

/// 各ファイルからパターンに一致する行を "file:line: text" 形式で列挙する
let private findViolations (files: string list) (pattern: Regex) : string list =
    [ for file in files do
          for i, line in File.ReadLines file |> Seq.indexed do
              if pattern.IsMatch line then
                  yield sprintf "%s:%d: %s" (Path.GetFileName file) (i + 1) (line.Trim()) ]

let private assertNoViolations (rule: string) (violations: string list) : unit =
    Assert.True(List.isEmpty violations, sprintf "%s に違反しています:\n  %s" rule (String.concat "\n  " violations))

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``Domain 層は壁時計・乱数など非決定的 API を使わない`` () =
    // Domain は純粋関数層 (AGENTS.md の DSL 解釈ルール)。時刻・乱数が必要なら
    // 呼び出し側 (Api/Infrastructure) で解決して引数で渡す
    let banned =
        Regex(@"\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b|\bGuid\.NewGuid\b|\bRandom\(|\bStopwatch\b")

    findViolations (sourceFiles "Domain") banned
    |> assertNoViolations "Domain 層の決定性 (壁時計/乱数/計時の直接使用禁止)"

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``Api 層は SQL を直接書かない (Donald は Infrastructure 専用)`` () =
    // SQL とスキーマのドリフト検査 (Repository round-trip / PREPARE 検証) は
    // Infrastructure 層のテストに集約している。Api 層に SQL が漏れると
    // その網から外れるため、Donald の使用自体を禁止する
    let banned = Regex(@"\bopen\s+Donald\b|\bDb\.newCommand\b")

    findViolations (sourceFiles "Api") banned
    |> assertNoViolations "Api 層の SQL 禁止 (Donald / Db.newCommand)"
