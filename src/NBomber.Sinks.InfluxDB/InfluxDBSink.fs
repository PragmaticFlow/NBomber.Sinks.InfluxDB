namespace NBomber.Sinks.InfluxDB

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

    let mapLatencyCount (context: IBaseContext) (tags: CustomTag[]) (scnStats: ScenarioStats) =

        let operation = getOperationName(scnStats.CurrentOperation)

        PointData.Measurement("nbomber")
            .Field("latency_count.less_or_eq_800", int64 scnStats.LatencyCount.LessOrEq800)
            .Field("latency_count.more_800_less_1200", int64 scnStats.LatencyCount.More800Less1200)
            .Field("latency_count.more_or_eq_1200", int64 scnStats.LatencyCount.MoreOrEq1200)
            .Tag("scenario", scnStats.ScenarioName)
            .Tag("operation", operation)
        |> addTestInfoTags context
        |> addCustomTags tags

    let mapStatusCodes (context: IBaseContext) (tags: CustomTag[]) (scnStats: ScenarioStats) =

        let operation = getOperationName(scnStats.CurrentOperation)

        scnStats.StatusCodes
        |> Array.map (fun x ->
            PointData
                .Measurement("nbomber")
                .Tag("scenario", scnStats.ScenarioName)
                .Tag("operation", operation)
                .Field("status_code.value", int64 x.StatusCode)
                .Field("status_code.count", int64 x.Count)
                .Field("status_code.is_error", x.IsError)
            |> addTestInfoTags context
            |> addCustomTags tags
        )

    let mapScenarioStats (context: IBaseContext) (tags: CustomTag[]) (scnStats: ScenarioStats) =
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

            [|("all.request.count", $"{stepStats.Ok.Request.Count + stepStats.Fail.Request.Count}")
              ("all.datatransfer.all", $"{stepStats.Ok.DataTransfer.AllBytes + stepStats.Fail.DataTransfer.AllBytes}")

              ("ok.request.count", $"{okR.Count}"); ("ok.request.rps", $"{okR.RPS}")
              ("ok.latency.min", $"{okL.MinMs}"); ("ok.latency.mean", $"{okL.MeanMs}")
              ("ok.latency.max", $"{okL.MaxMs}"); ("ok.latency.stddev", $"{okL.StdDev}")
              ("ok.latency.percent50", $"{okL.Percent50}"); ("ok.latency.percent75", $"{okL.Percent75}")
              ("ok.latency.percent95", $"{okL.Percent95}"); ("ok.latency.percent99", $"{okL.Percent99}")
              ("ok.datatransfer.min", $"{okD.MinBytes}"); ("ok.datatransfer.mean", $"{okD.MeanBytes}")
              ("ok.datatransfer.max", $"{okD.MaxBytes}"); ("ok.datatransfer.all", $"{okD.AllBytes}")
              ("ok.datatransfer.percent50", $"{okD.Percent50}"); ("ok.datatransfer.percent75", $"{okD.Percent75}")
              ("ok.datatransfer.percent95", $"{okD.Percent95}"); ("ok.datatransfer.percent99", $"{okD.Percent99}")

              ("fail.request.count", $"{fR.Count}"); ("fail.request.rps", $"{fR.RPS}")
              ("fail.latency.min", $"{fL.MinMs}"); ("fail.latency.mean", $"{fL.MeanMs}")
              ("fail.latency.max", $"{fL.MaxMs}"); ("fail.latency.stddev", $"{fL.StdDev}")
              ("fail.latency.percent50", $"{fL.Percent50}"); ("fail.latency.percent75", $"{fL.Percent75}")
              ("fail.latency.percent95", $"{fL.Percent95}"); ("fail.latency.percent99", $"{fL.Percent99}")
              ("fail.datatransfer.min", $"{fD.MinBytes}"); ("fail.datatransfer.mean", $"{fD.MeanBytes}")
              ("fail.datatransfer.max", $"{fD.MaxBytes}"); ("fail.datatransfer.all", $"{fD.AllBytes}")
              ("fail.datatransfer.percent50", $"{fD.Percent50}"); ("fail.datatransfer.percent75", $"{fD.Percent75}")
              ("fail.datatransfer.percent95", $"{fD.Percent95}"); ("fail.datatransfer.percent99", $"{fD.Percent99}")

              ("simulation.value", $"{simulation.Value}")|]

            |> Array.map (fun (name,value) ->
                PointData.Measurement("nbomber").Field(name, value)
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

                PointData.Measurement("nbomber").Field("start_session.session_id", _context.TestInfo.SessionId)
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
                |> Array.collect (mapScenarioStats _context _customTags)
                |> writeApi.WritePointsAsync
            )
            |> Option.defaultValue Task.CompletedTask

        member _.SaveFinalStats(stats: NodeStats[]) =
            _influxClient
            |> Option.map(fun client ->
                let writeApi = client.GetWriteApiAsync()

                let writeFinalStats =
                    stats
                    |> Array.collect (fun x -> x.ScenarioStats)
                    |> Array.collect (mapScenarioStats _context _customTags)
                    |> writeApi.WritePointsAsync

                let writeLatencyCount =
                    stats
                    |> Array.collect (fun x -> x.ScenarioStats)
                    |> Array.map (mapLatencyCount _context _customTags)
                    |> writeApi.WritePointsAsync

                let writeStatusCodes =
                    stats
                    |> Array.collect (fun x -> x.ScenarioStats)
                    |> Array.collect (mapStatusCodes _context _customTags)
                    |> writeApi.WritePointsAsync

                Task.WhenAll(writeFinalStats, writeLatencyCount, writeStatusCodes)
            )
            |> Option.defaultValue Task.CompletedTask

        member _.Stop() =
            _influxClient
            |> Option.map(fun client ->
                let writeApi = client.GetWriteApiAsync()

                PointData.Measurement("nbomber").Field("stop_session.session_id", _context.TestInfo.SessionId)
                |> addTestInfoTags _context
                |> addCustomTags _customTags
                |> writeApi.WritePointAsync
            )
            |> Option.defaultValue Task.CompletedTask

        member _.Dispose() = ()
