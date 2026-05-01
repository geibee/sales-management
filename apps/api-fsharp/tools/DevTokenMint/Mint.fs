module SalesManagement.Tools.DevTokenMint.Mint

open System
open System.IdentityModel.Tokens.Jwt
open System.Security.Claims
open System.Text
open Microsoft.IdentityModel.Tokens

type MintRequest =
    { SigningKey: string
      Audience: string
      Issuer: string
      Role: string
      User: string
      Ttl: TimeSpan }

let allowedRoles = [ "viewer"; "operator"; "admin" ]

let parseTtl (input: string) : TimeSpan =
    if String.IsNullOrWhiteSpace input then
        failwith "Invalid ttl: empty"
    else
        let value = input.Trim()
        let suffix = value.[value.Length - 1]
        let numeric = value.Substring(0, value.Length - 1)

        match Double.TryParse numeric with
        | false, _ -> failwithf "Invalid ttl: %s (use 30s / 5m / 1h / 7d)" value
        | true, n when n <= 0.0 -> failwithf "Invalid ttl: %s (must be positive)" value
        | true, n ->
            match suffix with
            | 's' -> TimeSpan.FromSeconds n
            | 'm' -> TimeSpan.FromMinutes n
            | 'h' -> TimeSpan.FromHours n
            | 'd' -> TimeSpan.FromDays n
            | _ -> failwithf "Invalid ttl suffix: %c (use s/m/h/d)" suffix

let mintToken (request: MintRequest) : string =
    if not (allowedRoles |> List.contains request.Role) then
        failwithf "Invalid role: %s (allowed: %s)" request.Role (String.concat ", " allowedRoles)

    let keyBytes = Encoding.UTF8.GetBytes request.SigningKey
    let key = SymmetricSecurityKey keyBytes
    let credentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    let realmAccessJson = sprintf """{"roles":["%s"]}""" request.Role

    let claims =
        [| Claim("sub", request.User)
           Claim("preferred_username", request.User)
           Claim("realm_access", realmAccessJson, JsonClaimValueTypes.Json) |]

    let token =
        JwtSecurityToken(
            issuer = request.Issuer,
            audience = request.Audience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow + request.Ttl),
            signingCredentials = credentials
        )

    JwtSecurityTokenHandler().WriteToken token
