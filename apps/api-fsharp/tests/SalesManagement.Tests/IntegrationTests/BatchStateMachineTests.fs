module SalesManagement.Tests.IntegrationTests.BatchStateMachineTests

open System
open System.IO
open System.Text.Json
open Xunit

let private fsharpRoot =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

let private loadStateMachine () : JsonElement =
    let path = Path.Combine(fsharpRoot, "localstack", "init", "state-machine.json")
    Assert.True(File.Exists path, sprintf "state-machine.json not found at %s" path)
    let json = File.ReadAllText path
    JsonDocument.Parse(json).RootElement

[<Fact>]
[<Trait("Category", "BatchStateMachine")>]
let ``state-machine.json defines RunBatch as start state with retry and catch`` () =
    let root = loadStateMachine ()
    Assert.Equal("RunBatch", root.GetProperty("StartAt").GetString())

    let states = root.GetProperty("States")
    let runBatch = states.GetProperty("RunBatch")

    Assert.Equal("Task", runBatch.GetProperty("Type").GetString())
    Assert.Equal("arn:aws:states:::lambda:invoke", runBatch.GetProperty("Resource").GetString())
    Assert.Equal("NotifySuccess", runBatch.GetProperty("Next").GetString())

    // Retry/Catch が定義されていること
    let retry = runBatch.GetProperty("Retry")
    Assert.True(retry.GetArrayLength() >= 1)
    let firstRetry = retry.[0]
    Assert.Equal(2, firstRetry.GetProperty("MaxAttempts").GetInt32())

    let catch = runBatch.GetProperty("Catch")
    Assert.True(catch.GetArrayLength() >= 1)
    Assert.Equal("NotifyFailure", catch.[0].GetProperty("Next").GetString())

[<Fact>]
[<Trait("Category", "BatchStateMachine")>]
let ``state-machine.json defines NotifySuccess and NotifyFailure as terminal SQS sendMessage tasks`` () =
    let root = loadStateMachine ()
    let states = root.GetProperty("States")

    for name, expectedStatus in [ "NotifySuccess", "SUCCESS"; "NotifyFailure", "FAILURE" ] do
        let s = states.GetProperty(name)
        Assert.Equal("Task", s.GetProperty("Type").GetString())
        Assert.Equal("arn:aws:states:::sqs:sendMessage", s.GetProperty("Resource").GetString())
        Assert.True(s.GetProperty("End").GetBoolean())

        let parameters = s.GetProperty("Parameters")
        let queueUrl = parameters.GetProperty("QueueUrl").GetString()
        Assert.Contains("batch-notifications", queueUrl)

        let body = parameters.GetProperty("MessageBody")
        Assert.Equal(expectedStatus, body.GetProperty("status").GetString())

[<Fact>]
[<Trait("Category", "BatchStateMachine")>]
let ``state-machine.json declares exactly the three required states`` () =
    let root = loadStateMachine ()
    let states = root.GetProperty("States")

    let names = [ for p in states.EnumerateObject() -> p.Name ] |> List.sort

    Assert.Equal<string list>([ "NotifyFailure"; "NotifySuccess"; "RunBatch" ], names)

[<Fact>]
[<Trait("Category", "BatchStateMachine")>]
let ``setup.sh provisions Step Functions state machine and EventBridge rule`` () =
    let path = Path.Combine(fsharpRoot, "localstack", "init", "setup.sh")
    Assert.True(File.Exists path, sprintf "setup.sh not found at %s" path)
    let sh = File.ReadAllText path

    // Step Functions ステートマシンの作成
    Assert.Contains("awslocal stepfunctions create-state-machine", sh)
    Assert.Contains("--name batch-orchestrator", sh)
    Assert.Contains("state-machine.json", sh)

    // EventBridge ルール (毎月1日 0:00 の cron)
    Assert.Contains("awslocal events put-rule", sh)
    Assert.Contains("--name monthly-close-schedule", sh)
    Assert.Contains("cron(0 0 1 * ? *)", sh)

    // EventBridge ターゲット (Step Functions ステートマシン)
    Assert.Contains("awslocal events put-targets", sh)
    Assert.Contains("--rule monthly-close-schedule", sh)
    Assert.Contains("monthly-close", sh)
