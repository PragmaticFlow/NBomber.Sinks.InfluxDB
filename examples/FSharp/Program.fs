open System
open System.Threading.Tasks
open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Contracts
open NBomber.FSharp
open NBomber.Sinks.InfluxDB

[<EntryPoint>]
let main argv =

    let random = Random()

    let login = Step.create("login", fun context -> task {

        let delay = random.Next(100, 110)
        do! Task.Delay(delay)

        if delay < 108 then
            return Response.ok(statusCode = 200, sizeBytes = delay)
        else
            return Response.fail(statusCode = 401, sizeBytes = delay)
    })

    let getProduct = Step.create("get_product", fun context -> task {

        let delay = random.Next(200, 250)
        do! Task.Delay(delay)

        if delay < 245 then
            return Response.ok(statusCode = 202, sizeBytes = delay)
        else
            return Response.fail(statusCode = 404, sizeBytes = delay)
    })

    let buyProduct = Step.create("buy_product", fun context -> task {

        let delay = random.Next(300, 350)
        do! Task.Delay(delay)

        if delay < 340 then
            return Response.ok(statusCode = 200, sizeBytes = delay)
        else
            return Response.fail(statusCode = 409, sizeBytes = delay)
    })

    use influxDb = new InfluxDBSink()

    Scenario.create "buy_one_product_scenario" [login; getProduct; buyProduct]
    |> Scenario.withoutWarmUp
    |> Scenario.withLoadSimulations [
        RampConstant(copies = 100, during = minutes 2)
        KeepConstant(copies = 100, during = minutes 3)
    ]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "eshop_test_suite"
    |> NBomberRunner.withTestName "purchase_test"
    |> NBomberRunner.withReportingSinks [influxDb]
    |> NBomberRunner.withReportingInterval(seconds 5)
    |> NBomberRunner.loadInfraConfig "infra-config.json"
    |> NBomberRunner.run
    |> ignore

    0 // return an integer exit code

