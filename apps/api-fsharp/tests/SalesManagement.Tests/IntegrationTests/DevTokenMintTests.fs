module SalesManagement.Tests.IntegrationTests.DevTokenMintTests

open System
open System.IdentityModel.Tokens.Jwt
open System.Security.Claims
open System.Text
open System.Text.Json
open Microsoft.IdentityModel.Tokens
open Xunit
open SalesManagement.Tools.DevTokenMint.Mint

let private signingKey = "stepf16-test-signing-key-please-do-not-use-in-production"
let private audience = "sales-api"
let private issuer = "stepf16-test"

let private defaultRequest =
    { SigningKey = signingKey
      Audience = audience
      Issuer = issuer
      Role = "operator"
      User = "u1"
      Ttl = TimeSpan.FromHours 1.0 }

let private validate (token: string) : ClaimsPrincipal =
    let handler = JwtSecurityTokenHandler()
    handler.InboundClaimTypeMap.Clear()

    let parameters =
        TokenValidationParameters(
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SymmetricSecurityKey(Encoding.UTF8.GetBytes signingKey),
            ClockSkew = TimeSpan.Zero
        )

    let mutable validated: SecurityToken = Unchecked.defaultof<_>
    handler.ValidateToken(token, parameters, &validated)

let private extractRoles (token: string) : string list =
    let parsed = JwtSecurityTokenHandler().ReadJwtToken token
    let claim = parsed.Claims |> Seq.tryFind (fun c -> c.Type = "realm_access")

    match claim with
    | None -> []
    | Some c ->
        use doc = JsonDocument.Parse c.Value
        let mutable roles = Unchecked.defaultof<JsonElement>

        if doc.RootElement.TryGetProperty("roles", &roles) then
            roles.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> Seq.toList
        else
            []

[<Trait("Category", "DevTokenMint")>]
[<Trait("Category", "Integration")>]
type MintTests() =

    [<Fact>]
    member _.``minted token validates with the same signing key``() =
        let token = mintToken defaultRequest
        let principal = validate token
        Assert.NotNull principal
        let sub = principal.FindFirst "sub"
        Assert.NotNull sub
        Assert.Equal("u1", sub.Value)

    [<Fact>]
    member _.``role appears in realm_access.roles claim``() =
        let token =
            mintToken
                { defaultRequest with
                    Role = "operator" }

        let roles = extractRoles token
        Assert.Equal<string list>([ "operator" ], roles)

    [<Fact>]
    member _.``minting with disallowed role fails``() =
        Assert.Throws<Exception>(fun () ->
            mintToken
                { defaultRequest with
                    Role = "superuser" }
            |> ignore)
        |> ignore

    [<Fact>]
    member _.``parseTtl handles s/m/h/d``() =
        Assert.Equal(TimeSpan.FromSeconds 30.0, parseTtl "30s")
        Assert.Equal(TimeSpan.FromMinutes 5.0, parseTtl "5m")
        Assert.Equal(TimeSpan.FromHours 2.0, parseTtl "2h")
        Assert.Equal(TimeSpan.FromDays 7.0, parseTtl "7d")

    [<Fact>]
    member _.``parseTtl rejects junk``() =
        Assert.Throws<Exception>(fun () -> parseTtl "abc" |> ignore) |> ignore
        Assert.Throws<Exception>(fun () -> parseTtl "0s" |> ignore) |> ignore
        Assert.Throws<Exception>(fun () -> parseTtl "10x" |> ignore) |> ignore

    [<Fact>]
    member _.``short ttl produces a token expiring soon``() =
        let token =
            mintToken
                { defaultRequest with
                    Ttl = TimeSpan.FromSeconds 30.0 }

        let parsed = JwtSecurityTokenHandler().ReadJwtToken token
        let lifetime = parsed.ValidTo - DateTime.UtcNow
        Assert.True(lifetime <= TimeSpan.FromSeconds 35.0, sprintf "lifetime=%A" lifetime)
        Assert.True(lifetime >= TimeSpan.FromSeconds 25.0, sprintf "lifetime=%A" lifetime)

    [<Fact>]
    member _.``Program.run with --role operator --user u1 emits a JWT``() =
        let argv =
            [| "--role"
               "operator"
               "--user"
               "u1"
               "--signing-key"
               signingKey
               "--issuer"
               issuer |]

        let writer = new System.IO.StringWriter()
        let original = Console.Out
        Console.SetOut writer

        try
            let exitCode = SalesManagement.Tools.DevTokenMint.Program.run argv

            Assert.Equal(0, exitCode)
            let output = writer.ToString().Trim()
            Assert.StartsWith("eyJ", output)
            // Validate the emitted token round-trips.
            let principal = validate output
            let sub = principal.FindFirst "sub"
            Assert.Equal("u1", sub.Value)
            let roles = extractRoles output
            Assert.Equal<string list>([ "operator" ], roles)
        finally
            Console.SetOut original

    [<Fact>]
    member _.``Program.run defaults to viewer when --role omitted``() =
        let argv = [| "--user"; "anyone"; "--signing-key"; signingKey; "--issuer"; issuer |]
        let writer = new System.IO.StringWriter()
        let original = Console.Out
        Console.SetOut writer

        try
            let exitCode = SalesManagement.Tools.DevTokenMint.Program.run argv

            Assert.Equal(0, exitCode)
            let output = writer.ToString().Trim()
            let roles = extractRoles output
            Assert.Equal<string list>([ "viewer" ], roles)
        finally
            Console.SetOut original
