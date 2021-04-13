open System.Threading.Tasks
open FSharp.Control.Tasks.NonAffine
open NBomber.Contracts
open NBomber.FSharp
open NBomber.Sinks.InfluxDB

[<EntryPoint>]
let main argv =

    let step = Step.create("step_1", fun context -> task {

        do! Task.Delay(milliseconds 100)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.ok()
    })

    use influxDb = new InfluxDBSink()

    Scenario.create "hello_world_scenario" [step]
    |> Scenario.withoutWarmUp
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "reporting"
    |> NBomberRunner.withTestName "influx_test"
    |> NBomberRunner.withReportingSinks [influxDb]
    |> NBomberRunner.withReportingInterval(seconds 10)
    |> NBomberRunner.loadInfraConfig "infra-config.json"
    |> NBomberRunner.run
    |> ignore

    0 // return an integer exit code

