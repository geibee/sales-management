module SalesManagement.Tests.IntegrationTests.OpenApiSchemaTests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit

let private openapiPath =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "openapi.yaml"))

let private readOpenapi () : string =
    if not (File.Exists openapiPath) then
        failwithf "openapi.yaml not found at %s" openapiPath

    File.ReadAllText openapiPath

let private requiredSchemas =
    [ "LotResponse"
      "CreateLotResponse"
      "LotStatus"
      "LotSummary"
      "LotsListResponse"
      "SalesCaseResponse"
      "CreatedSalesCaseResponse"
      "SalesCaseSummary"
      "SalesCasesListResponse"
      "ReservationCaseResponse"
      "ReservationStatusResponse"
      "ConsignmentCaseResponse"
      "ConsignmentStatusResponse"
      "PriceCheckResponse" ]

[<Fact>]
[<Trait("Category", "OpenApiSchema")>]
[<Trait("Category", "Integration")>]
let ``openapi.yaml defines all required response schemas`` () =
    let body = readOpenapi ()

    for name in requiredSchemas do
        let pattern = sprintf "\\n    %s:" name
        Assert.True(Regex.IsMatch(body, pattern), sprintf "schema '%s' must be defined under components.schemas" name)

[<Fact>]
[<Trait("Category", "OpenApiSchema")>]
[<Trait("Category", "Integration")>]
let ``every 200 application/json response references a schema via $ref`` () =
    let body = readOpenapi ()
    let lines = body.Split('\n')

    let mutable i = 0

    while i < lines.Length do
        let line = lines.[i]

        if line.TrimEnd().EndsWith("'200':") then
            let indent = line.Length - line.TrimStart().Length
            let mutable j = i + 1
            let mutable hasJson = false
            let mutable hasRef = false

            while j < lines.Length
                  && (lines.[j].Length = 0
                      || (lines.[j].Length - lines.[j].TrimStart().Length) > indent) do
                let l = lines.[j]

                if l.Contains "application/json:" then
                    hasJson <- true

                if hasJson && l.Contains "$ref:" then
                    hasRef <- true

                j <- j + 1

            if hasJson then
                Assert.True(
                    hasRef,
                    sprintf "200 application/json response at line %d should use $ref to a component schema" (i + 1)
                )

        i <- i + 1

[<Fact>]
[<Trait("Category", "OpenApiSchema")>]
[<Trait("Category", "Integration")>]
let ``PriceCheckResponse uses camelCase fields`` () =
    let body = readOpenapi ()

    let priceCheckBlock =
        let idx = body.IndexOf("PriceCheckResponse:")
        Assert.True(idx >= 0, "PriceCheckResponse schema must be present")
        let rest = body.Substring(idx)
        // Block ends at the next 4-space schema entry, or at the next top-level (0-indent) section
        let nextSchema = Regex.Match(rest.Substring(1), "\\n    [A-Z][A-Za-z0-9]+:")
        let nextTop = Regex.Match(rest.Substring(1), "\\n[a-zA-Z]")

        let endIdx =
            [ if nextSchema.Success then
                  yield nextSchema.Index + 1
              if nextTop.Success then
                  yield nextTop.Index + 1 ]
            |> List.sort
            |> List.tryHead
            |> Option.defaultValue rest.Length

        rest.Substring(0, endIdx)

    Assert.Contains("basePrice:", priceCheckBlock)
    Assert.Contains("adjustmentRate:", priceCheckBlock)
    Assert.Contains("source:", priceCheckBlock)
    Assert.DoesNotContain("BasePrice:", priceCheckBlock)
    Assert.DoesNotContain("AdjustmentRate:", priceCheckBlock)

[<Fact>]
[<Trait("Category", "OpenApiSchema")>]
[<Trait("Category", "Integration")>]
let ``LotStatus enum lists all five states`` () =
    let body = readOpenapi ()
    Assert.Contains("manufacturing", body)
    Assert.Contains("manufactured", body)
    Assert.Contains("shipping_instructed", body)
    Assert.Contains("shipped", body)
    Assert.Contains("conversion_instructed", body)
