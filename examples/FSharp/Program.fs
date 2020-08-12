open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive

open NBomber.Contracts
open NBomber.FSharp
open NBomber.Sinks.InfluxDB

let run () =

    let step = Step.create("step_1", fun context -> task {

        do! Task.Delay(seconds 1)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.Ok()
    })

    use influxDb = new InfluxDBSink()

    Scenario.create "hello_world_scenario" [step]
    |> Scenario.withoutWarmUp
    |> Scenario.withLoadSimulations [KeepConstant(copies = 1, during = minutes 1)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "reporting"
    |> NBomberRunner.withTestName "influx_test"
    |> NBomberRunner.withReportingSinks([influxDb], seconds 10)
    |> NBomberRunner.loadInfraConfig "infra-config.json"
    |> NBomberRunner.run
    |> ignore

[<EntryPoint>]
let main argv =

    run()

    0 // return an integer exit code

