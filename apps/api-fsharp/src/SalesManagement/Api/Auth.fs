module SalesManagement.Api.Auth

open System
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.IdentityModel.Tokens
open Giraffe

type AuthOptions =
    { Enabled: bool
      Authority: string option
      Audience: string
      SigningKey: string option
      RequireHttpsMetadata: bool }

let private parseBool (value: string) =
    match value with
    | null
    | "" -> None
    | v ->
        match Boolean.TryParse v with
        | true, b -> Some b
        | _ -> None

let read (config: IConfiguration) : AuthOptions =
    let section = config.GetSection("Authentication")

    let getOpt (key: string) =
        match section.[key] with
        | null
        | "" -> None
        | v -> Some v

    { Enabled = parseBool section.["Enabled"] |> Option.defaultValue false
      Authority = getOpt "Authority"
      Audience = getOpt "Audience" |> Option.defaultValue "sales-api"
      SigningKey = getOpt "SigningKey"
      RequireHttpsMetadata = parseBool section.["RequireHttpsMetadata"] |> Option.defaultValue false }

let private rolesFromArray (roles: JsonElement) : Set<string> =
    if roles.ValueKind <> JsonValueKind.Array then
        Set.empty
    else
        let pickString (e: JsonElement) =
            if e.ValueKind = JsonValueKind.String then
                Some(e.GetString())
            else
                None

        roles.EnumerateArray() |> Seq.choose pickString |> Set.ofSeq

let private parseRealmAccess (raw: string) : Set<string> =
    try
        use doc = JsonDocument.Parse raw
        let mutable roles = doc.RootElement

        if doc.RootElement.TryGetProperty("roles", &roles) then
            rolesFromArray roles
        else
            Set.empty
    with _ ->
        Set.empty

let private extractRealmRoles (principal: ClaimsPrincipal) : Set<string> =
    let claim = principal.FindFirst("realm_access")

    if isNull claim || String.IsNullOrEmpty claim.Value then
        Set.empty
    else
        parseRealmAccess claim.Value

type RealmRoleRequirement(acceptable: string list) =
    interface IAuthorizationRequirement
    member _.Acceptable = acceptable

type RealmRoleHandler() =
    inherit AuthorizationHandler<RealmRoleRequirement>()

    override _.HandleRequirementAsync(ctx, req) =
        let userRoles = extractRealmRoles ctx.User

        if req.Acceptable |> List.exists userRoles.Contains then
            ctx.Succeed req

        Task.CompletedTask

let policyForViewer = "viewer"
let policyForOperator = "operator"
let policyForAdmin = "admin"

let private acceptableRoles (policy: string) =
    match policy with
    | "viewer" -> [ "viewer"; "operator"; "admin" ]
    | "operator" -> [ "operator"; "admin" ]
    | "admin" -> [ "admin" ]
    | _ -> []

let configure (services: IServiceCollection) (opts: AuthOptions) : unit =
    if not opts.Enabled then
        ()
    else
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(fun (jwtOpts: JwtBearerOptions) ->
                jwtOpts.MapInboundClaims <- false
                jwtOpts.RequireHttpsMetadata <- opts.RequireHttpsMetadata

                match opts.SigningKey with
                | Some keyMaterial ->
                    let keyBytes = Encoding.UTF8.GetBytes keyMaterial
                    let key = SymmetricSecurityKey keyBytes

                    jwtOpts.TokenValidationParameters <-
                        TokenValidationParameters(
                            ValidateIssuer = false,
                            ValidateAudience = true,
                            ValidAudience = opts.Audience,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = key,
                            ClockSkew = TimeSpan.Zero,
                            NameClaimType = "preferred_username",
                            RoleClaimType = "roles"
                        )
                | None ->
                    jwtOpts.Audience <- opts.Audience

                    match opts.Authority with
                    | Some authority -> jwtOpts.Authority <- authority
                    | None -> ())
        |> ignore

        let registerPolicy (authOpts: AuthorizationOptions) (policy: string) =
            let configurePolicy (builder: AuthorizationPolicyBuilder) =
                builder.RequireAuthenticatedUser() |> ignore
                builder.AddRequirements(RealmRoleRequirement(acceptableRoles policy)) |> ignore

            authOpts.AddPolicy(policy, Action<AuthorizationPolicyBuilder> configurePolicy)

        let registerAllPolicies (authOpts: AuthorizationOptions) =
            for policy in [ policyForViewer; policyForOperator; policyForAdmin ] do
                registerPolicy authOpts policy

        services.AddAuthorization(Action<AuthorizationOptions> registerAllPolicies)
        |> ignore

        services.AddSingleton<IAuthorizationHandler, RealmRoleHandler>() |> ignore

let unauthorizedHandler: HttpHandler =
    fun _ (ctx: HttpContext) -> task {
        if not ctx.Response.HasStarted then
            ctx.Response.StatusCode <- 401
            ctx.Response.Headers.["WWW-Authenticate"] <- Microsoft.Extensions.Primitives.StringValues "Bearer"
            ctx.Response.ContentType <- "application/problem+json"

            do!
                ctx.Response.WriteAsync
                    """{"type":"unauthorized","title":"Unauthorized","status":401,"detail":"Authentication required."}"""

        return Some ctx
    }

let forbiddenHandler: HttpHandler =
    fun _ (ctx: HttpContext) -> task {
        if not ctx.Response.HasStarted then
            ctx.Response.StatusCode <- 403
            ctx.Response.ContentType <- "application/problem+json"

            do!
                ctx.Response.WriteAsync
                    """{"type":"forbidden","title":"Forbidden","status":403,"detail":"Insufficient role."}"""

        return Some ctx
    }

let private noopHandler: HttpHandler = fun next ctx -> next ctx

let private requirePolicy (enabled: bool) (policy: string) : HttpHandler =
    if not enabled then
        noopHandler
    else
        requiresAuthentication unauthorizedHandler
        >=> authorizeByPolicyName policy forbiddenHandler

let requireViewer (enabled: bool) : HttpHandler = requirePolicy enabled policyForViewer
let requireOperator (enabled: bool) : HttpHandler = requirePolicy enabled policyForOperator
let requireAdmin (enabled: bool) : HttpHandler = requirePolicy enabled policyForAdmin

let tryGetUserId (ctx: HttpContext) : string option =
    let principal = ctx.User

    if isNull (box principal) then
        None
    else
        let claim = principal.FindFirst("sub")

        if isNull claim || String.IsNullOrEmpty claim.Value then
            None
        else
            Some claim.Value

let getUserIdOrSystem (ctx: HttpContext) : string =
    tryGetUserId ctx |> Option.defaultValue "system"
