module SalesManagement.Tests.Architecture.ArchitectureTests

open Xunit
open ArchUnitNET.Domain
open ArchUnitNET.Loader
open ArchUnitNET.Fluent
open ArchUnitNET.xUnit

let private architecture =
    ArchLoader().LoadAssemblies(typeof<SalesManagement.Domain.Types.NonEmptyList<int>>.Assembly).Build()

let private layer (name: string) (ns: string) : IObjectProvider<IType> =
    ArchRuleDefinition.Types().That().ResideInNamespace(ns).As(name) :> _

let private domain = layer "Domain Layer" "SalesManagement.Domain"

let private infrastructure =
    layer "Infrastructure Layer" "SalesManagement.Infrastructure"

let private api = layer "Api Layer" "SalesManagement.Api"

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``domain does not depend on infrastructure`` () =
    let rule =
        ArchRuleDefinition
            .Types()
            .That()
            .ResideInNamespace("SalesManagement.Domain")
            .Should()
            .NotDependOnAny(infrastructure)

    rule.Check(architecture)

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``domain does not depend on api`` () =
    let rule =
        ArchRuleDefinition.Types().That().ResideInNamespace("SalesManagement.Domain").Should().NotDependOnAny(api)

    rule.Check(architecture)

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``repositories live in infrastructure`` () =
    // F# モジュール初期化用 startup-code 型 (<StartupCode$...>.$<...>) は除外する
    let rule =
        ArchRuleDefinition
            .Types()
            .That()
            .HaveNameEndingWith("Repository")
            .And()
            .DoNotHaveFullNameContaining("StartupCode")
            .Should()
            .ResideInNamespace("SalesManagement.Infrastructure")

    rule.Check(architecture)

[<Fact>]
[<Trait("Category", "Architecture")>]
let ``infrastructure does not depend on api`` () =
    let rule =
        ArchRuleDefinition
            .Types()
            .That()
            .ResideInNamespace("SalesManagement.Infrastructure")
            .Should()
            .NotDependOnAny(api)

    rule.Check(architecture)

// Suppress unused-binding warning for layer helper consumers
let private _layers = (domain, infrastructure, api)
