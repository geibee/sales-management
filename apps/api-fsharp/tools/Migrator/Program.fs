open DbUp
open System

[<EntryPoint>]
let main args =
    let connectionString =
        match Environment.GetEnvironmentVariable("DATABASE_URL") with
        | null -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
        | url -> url

    let upgrader =
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsFromFileSystem("migrations")
            .LogToConsole()
            .Build()

    let result = upgrader.PerformUpgrade()

    if result.Successful then
        printfn "マイグレーション完了"
        0
    else
        eprintfn "マイグレーション失敗: %s" (result.Error.ToString())
        1
