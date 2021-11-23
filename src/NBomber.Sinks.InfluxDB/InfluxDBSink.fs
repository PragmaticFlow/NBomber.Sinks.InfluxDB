namespace NBomber.Sinks.InfluxDB

open System.Collections.Generic
open System.Runtime.InteropServices
open System.Threading.Tasks

open InfluxDB.Client
open InfluxDB.Client.Writes
open Serilog
open Microsoft.Extensions.Configuration

open NBomber.Contracts
open NBomber.Contracts.Stats

[<CLIMutable>]
type CustomTag = { Key: string; Value: string }

[<CLIMutable>]
type InfluxDbSinkConfig = {
    Url: string
    Database: string
    UserName: string
    Password: string
    CustomTags: CustomTag[]
} with
    [<CompiledName("Create")>]
    static member create(url: string,
                         database: string,
                         [<Optional;DefaultParameterValue("")>] userName: string,
                         [<Optional;DefaultParameterValue("")>] password: string,
                         [<Optional;DefaultParameterValue(null:CustomTag[])>] customTags: CustomTag[]) =

        let tags = if isNull customTags then Array.empty else customTags
        { Url = url; Database = database; UserName = userName; Password = password; CustomTags = tags }

type InfluxDBSink(influxClient: InfluxDBClient, customTags: CustomTag[]) =

    let mutable _logger = Unchecked.defaultof<ILogger>
    let mutable _context = Unchecked.defaultof<IBaseContext>
    let mutable _influxClient = influxClient |> Option.ofObj
    let mutable _customTags = if isNull customTags then Array.empty else customTags

    let getOperationName (operation: OperationType) =
        match operation with
        | OperationType.Bombing  -> "bombing"
        | OperationType.Complete -> "complete"
        | _                      -> "bombing"

    let addCustomTags (tags: CustomTag[]) (point: PointData) =
        tags
        |> Array.fold (fun (p:PointData) t -> p.Tag(t.Key, t.Value)) point

    let addTestInfoTags (context: IBaseContext) (point: PointData) =
        let nodeType = context.NodeInfo.NodeType.ToString()
        let testInfo = context.TestInfo

        point
            .Tag("node_type", nodeType)
            .Tag("test_suite", testInfo.TestSuite)
            .Tag("test_name", testInfo.TestName)

    let mapToPoints (context: IBaseContext) (tags: CustomTag[]) (scnStats: ScenarioStats) =
        let operation = getOperationName(scnStats.CurrentOperation)
        let simulation = scnStats.LoadSimulationStats

        let addScenarioInfoTags (stepName) (point: PointData) =
            point
                .Tag("scenario", scnStats.ScenarioName)
                .Tag("step", stepName)
                .Tag("operation", operation)
                .Tag("simulation.name", simulation.SimulationName)

        scnStats.StepStats
        |> Array.collect (fun stepStats ->
            let okR = stepStats.Ok.Request
            let okL = stepStats.Ok.Latency
            let okD = stepStats.Ok.DataTransfer

            let fR = stepStats.Fail.Request
            let fL = stepStats.Fail.Latency
            let fD = stepStats.Fail.DataTransfer

            [|("nbomber__all.request.count", $"{stepStats.Ok.Request.Count + stepStats.Fail.Request.Count}")
              ("nbomber__all.datatransfer.all", $"{stepStats.Ok.DataTransfer.AllBytes + stepStats.Fail.DataTransfer.AllBytes}")

              ("nbomber__ok.request.count", $"{okR.Count}"); ("nbomber__ok.request.rps", $"{okR.RPS}")
              ("nbomber__ok.latency.min", $"{okL.MinMs}"); ("nbomber__ok.latency.mean", $"{okL.MeanMs}")
              ("nbomber__ok.latency.max", $"{okL.MaxMs}"); ("nbomber__ok.latency.stddev", $"{okL.StdDev}")
              ("nbomber__ok.latency.percent50", $"{okL.Percent50}"); ("nbomber__ok.latency.percent75", $"{okL.Percent75}")
              ("nbomber__ok.latency.percent95", $"{okL.Percent95}"); ("nbomber__ok.latency.percent99", $"{okL.Percent99}")
              ("nbomber__ok.datatransfer.min", $"{okD.MinBytes}"); ("nbomber__ok.datatransfer.mean", $"{okD.MeanBytes}")
              ("nbomber__ok.datatransfer.max", $"{okD.MaxBytes}"); ("nbomber__ok.datatransfer.all", $"{okD.AllBytes}")

              ("nbomber__fail.request.count", $"{fR.Count}"); ("nbomber__fail.request.rps", $"{fR.RPS}")
              ("nbomber__fail.latency.min", $"{fL.MinMs}"); ("nbomber__fail.latency.mean", $"{fL.MeanMs}")
              ("nbomber__fail.latency.max", $"{fL.MaxMs}"); ("nbomber__fail.latency.stddev", $"{fL.StdDev}")
              ("nbomber__fail.latency.percent50", $"{fL.Percent50}"); ("nbomber__fail.latency.percent75", $"{fL.Percent75}")
              ("nbomber__fail.latency.percent95", $"{fL.Percent95}"); ("nbomber__fail.latency.percent99", $"{fL.Percent99}")
              ("nbomber__fail.datatransfer.min", $"{fD.MinBytes}"); ("nbomber__fail.datatransfer.mean", $"{fD.MeanBytes}")
              ("nbomber__fail.datatransfer.max", $"{fD.MaxBytes}"); ("nbomber__fail.datatransfer.all", $"{fD.AllBytes}")

              ("nbomber__simulation.value", $"{simulation.Value}")|]

            |> Array.map (fun (name,value) ->
                PointData.Measurement(name).Field("value", value)
                |> addTestInfoTags context
                |> addScenarioInfoTags stepStats.StepName
                |> addCustomTags tags
            )
        )

    static let createClientFromConfig (config: InfluxDbSinkConfig) =
        InfluxDBClientFactory.CreateV1(
            config.Url,
            config.UserName,
            config.Password.ToCharArray(),
            config.Database,
            retentionPolicy = "autogen"
        )

    new (config: InfluxDbSinkConfig) =
        new InfluxDBSink(createClientFromConfig config, Array.empty)

    new() = new InfluxDBSink(null, Array.empty)

    member _.InfluxClient = _influxClient |> Option.defaultValue(Unchecked.defaultof<InfluxDBClient>)
    member _.CustomTags = _customTags

    interface IReportingSink with
        member _.SinkName = "NBomber.Sinks.InfluxDB"

        member _.Init(context: IBaseContext, infraConfig: IConfiguration) =
            _logger <- context.Logger.ForContext<InfluxDBSink>()
            _context <- context

            infraConfig
            |> Option.ofObj
            |> Option.map (fun x -> x.GetSection("InfluxDBSink").Get<InfluxDbSinkConfig>())
            |> Option.bind (fun x ->
                if not(x |> box |> isNull) then Some x
                else None
            )
            |> Option.iter (fun config ->
                _influxClient <- Some (createClientFromConfig config)
                _customTags <- if not (isNull config.CustomTags) then config.CustomTags else _customTags
            )

            Task.CompletedTask

        member _.Start() =
            _influxClient
            |> Option.map(fun client ->
                let writeApi = client.GetWriteApiAsync()

                PointData.Measurement("nbomber__start_session").Field("session_id", _context.TestInfo.SessionId)
                |> addTestInfoTags _context
                |> addCustomTags _customTags
                |> writeApi.WritePointAsync
            )
            |> Option.defaultValue Task.CompletedTask

        member _.SaveRealtimeStats(stats: ScenarioStats[]) =
            _influxClient
            |> Option.map(fun client ->
                let writeApi = client.GetWriteApiAsync()

                stats
                |> Array.collect (mapToPoints _context _customTags)
                |> writeApi.WritePointsAsync
            )
            |> Option.defaultValue Task.CompletedTask

        member _.SaveFinalStats(stats: NodeStats[]) = Task.CompletedTask

        member _.Stop() =
            _influxClient
            |> Option.map(fun client ->
                let writeApi = client.GetWriteApiAsync()

                PointData.Measurement("nbomber__stop_session").Field("session_id", _context.TestInfo.SessionId)
                |> addTestInfoTags _context
                |> addCustomTags _customTags
                |> writeApi.WritePointAsync
            )
            |> Option.defaultValue Task.CompletedTask

        member _.Dispose() = ()
