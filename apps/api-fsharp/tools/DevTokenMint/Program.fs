module SalesManagement.Tools.DevTokenMint.Program

open System
open System.IO
open Microsoft.Extensions.Configuration
open SalesManagement.Tools.DevTokenMint.Mint

let private usage =
    """Usage: dotnet run --project tools/DevTokenMint -- [options]

Options:
  --role <viewer|operator|admin>   Default: viewer
  --user <username>                Default: dev-user
  --ttl <duration>                 Default: 1h. Examples: 30s, 5m, 1h, 7d
  --signing-key <key>              Override SigningKey (also via env Authentication__SigningKey)
  --audience <aud>                 Override audience. Default: from appsettings.json or "sales-api"
  --issuer <iss>                   Default: dev-token-mint
  -h | --help                      Show this message
"""

let rec private parseArgs (acc: Map<string, string>) (argv: string list) : Map<string, string> =
    match argv with
    | [] -> acc
    | "-h" :: _
    | "--help" :: _ -> acc.Add("help", "1")
    | flag :: value :: rest when flag.StartsWith("--") ->
        let key = flag.Substring 2
        parseArgs (acc.Add(key, value)) rest
    | unexpected :: _ -> failwithf "Unexpected argument: %s" unexpected

let private locateSettingsDir () : string option =
    let mutable dir = DirectoryInfo(AppContext.BaseDirectory)
    let mutable found: string option = None

    while not (isNull dir) && found.IsNone do
        let candidate = Path.Combine(dir.FullName, "src", "SalesManagement", "appsettings.json")

        if File.Exists candidate then
            found <- Some(Path.Combine(dir.FullName, "src", "SalesManagement"))
        else
            dir <- dir.Parent

    found

let private buildConfig () : IConfiguration =
    let builder = ConfigurationBuilder()

    match locateSettingsDir () with
    | Some dir ->
        builder.SetBasePath dir |> ignore
        builder.AddJsonFile("appsettings.json", optional = true, reloadOnChange = false) |> ignore
        builder.AddJsonFile("appsettings.Development.json", optional = true, reloadOnChange = false) |> ignore
    | None -> ()

    builder.AddEnvironmentVariables() |> ignore
    builder.Build() :> IConfiguration

let private resolveSigningKey (config: IConfiguration) (cliOverride: string option) : string =
    match cliOverride with
    | Some k when not (String.IsNullOrWhiteSpace k) -> k
    | _ ->
        match config.["Authentication:SigningKey"] with
        | null
        | "" ->
            failwith
                "SigningKey not found. Set Authentication:SigningKey in appsettings.json (Development) or pass --signing-key / Authentication__SigningKey env."
        | v -> v

let private resolveAudience (config: IConfiguration) (cliOverride: string option) : string =
    match cliOverride with
    | Some a when not (String.IsNullOrWhiteSpace a) -> a
    | _ ->
        match config.["Authentication:Audience"] with
        | null
        | "" -> "sales-api"
        | v -> v

let run (argv: string[]) : int =
    try
        let parsed = parseArgs Map.empty (Array.toList argv)

        if parsed.ContainsKey "help" then
            printfn "%s" usage
            0
        else
            let role = parsed.TryFind "role" |> Option.defaultValue "viewer"
            let user = parsed.TryFind "user" |> Option.defaultValue "dev-user"
            let ttl = parseTtl (parsed.TryFind "ttl" |> Option.defaultValue "1h")
            let issuer = parsed.TryFind "issuer" |> Option.defaultValue "dev-token-mint"
            let cliKey = parsed.TryFind "signing-key"
            let cliAud = parsed.TryFind "audience"

            let config = buildConfig ()
            let signingKey = resolveSigningKey config cliKey
            let audience = resolveAudience config cliAud

            let token =
                mintToken
                    { SigningKey = signingKey
                      Audience = audience
                      Issuer = issuer
                      Role = role
                      User = user
                      Ttl = ttl }

            printfn "%s" token
            0
    with ex ->
        eprintfn "Error: %s" ex.Message
        1

[<EntryPoint>]
let main argv = run argv
