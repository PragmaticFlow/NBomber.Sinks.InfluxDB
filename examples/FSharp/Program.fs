open System.Threading.Tasks
open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Contracts
open NBomber.FSharp
open NBomber.Sinks.InfluxDB

[<EntryPoint>]
let main argv =

    let step1 = Step.create("step_1", fun context -> task {

        do! Task.Delay(milliseconds 100)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.ok(statusCode = 200, sizeBytes = 100)
    })

    let step2 = Step.create("step_2", fun context -> task {

        do! Task.Delay(milliseconds 300)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.ok(statusCode = 500, sizeBytes = 500)
    })

    let step3 = Step.create("step_3", fun context -> task {

        do! Task.Delay(milliseconds 300)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.ok(statusCode = 700, sizeBytes = 500)
    })

    use influxDb = new InfluxDBSink()

    Scenario.create "hello_world_scenario" [step1; step2; step3]
    |> Scenario.withoutWarmUp
    |> Scenario.withLoadSimulations [KeepConstant(10, minutes 2)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "reporting"
    |> NBomberRunner.withTestName "influx_test"
    |> NBomberRunner.withReportingSinks [influxDb]
    |> NBomberRunner.withReportingInterval(seconds 5)
    |> NBomberRunner.loadInfraConfig "infra-config.json"
    |> NBomberRunner.run
    |> ignore

    0 // return an integer exit code

