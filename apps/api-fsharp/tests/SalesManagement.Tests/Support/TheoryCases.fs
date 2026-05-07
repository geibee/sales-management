module SalesManagement.Tests.Support.TheoryCases

/// `[<MemberData>]` 用の `obj[]` を 2/3 タプルから生成するヘルパー。
/// `let case = tcase3` のように local alias して、S2 系で全テストを 1 行表現に揃える。
let tcase2 (a: 'a) (b: 'b) : obj[] = [| box a; box b |]

let tcase3 (a: 'a) (b: 'b) (c: 'c) : obj[] = [| box a; box b; box c |]

let tcase4 (a: 'a) (b: 'b) (c: 'c) (d: 'd) : obj[] = [| box a; box b; box c; box d |]
