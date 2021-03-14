namespace NBomber.Sinks.InfluxDB

open System.Threading.Tasks

open Serilog
open App.Metrics
open App.Metrics.Gauge
open Microsoft.Extensions.Configuration

open NBomber.Contracts

[<CLIMutable>]
type InfluxDbSinkConfig = {
    Url: string
    DbName: string
} with
    [<CompiledName("Create")>]
    static member create(url: string, dbName: string) =
        { Url = url; DbName = dbName }

type InfluxDBSink(metricsRoot: IMetricsRoot) =

    let mutable _logger = Unchecked.defaultof<ILogger>
    let mutable _metricsRoot = metricsRoot |> Option.ofObj
    let mutable _context = Unchecked.defaultof<IBaseContext>

    let getOperationName (operation: OperationType) =
        match operation with
        | OperationType.Bombing  -> "bombing"
        | OperationType.Complete -> "complete"
        | _                      -> "bombing"

    let saveScenarioStats (stats: ScenarioStats) =
        let operation = getOperationName(stats.CurrentOperation)
        let nodeType = _context.NodeInfo.NodeType.ToString()
        let testInfo = _context.TestInfo
        let simulation = stats.LoadSimulationStats

        stats.StepStats
        |> Array.iter(fun s ->
            try
                let lt = s.Ok.Latency
                let dt = s.Ok.DataTransfer

                [("OkCount", float s.Ok.Request.Count); ("FailCount", float s.Fail.Request.Count); ("RPS", float s.Ok.Request.RPS)
                 ("Min", float lt.MinMs); ("Mean", float lt.MeanMs); ("Max", float lt.MaxMs); ("StdDev", float lt.StdDev)
                 ("Percent50", float lt.Percent50); ("Percent75", float lt.Percent75); ("Percent95", float lt.Percent95); ("Percent99", float lt.Percent99)
                 ("MinDataKb", dt.MinKb); ("MeanDataKb", dt.MeanKb); ("MaxDataKb", dt.MaxKb); ("AllDataMB", dt.AllMB)
                 ("LoadSimulationValue", float simulation.Value)]

                |> List.iter(fun (name, value) ->
                    let metric =
                        GaugeOptions(
                            Name = name,
                            Context = "NBomber",
                            Tags = MetricTags([|"node_type"; "test_suite"; "test_name"
                                                "scenario"; "step"; "operation"; "simulation"|],
                                              [|nodeType; testInfo.TestSuite; testInfo.TestName
                                                stats.ScenarioName; s.StepName; operation; simulation.SimulationName|]))
                    _metricsRoot
                    |> Option.iter(fun x -> x.Measure.Gauge.SetValue(metric, value))
                )
            with
            | ex -> _logger.Error(ex.ToString())
        )

    new (config: InfluxDbSinkConfig) =
        let metrics = MetricsBuilder().Report.ToInfluxDb(config.Url, config.DbName).Build()
        new InfluxDBSink(metrics)

    new() = new InfluxDBSink(null)

    interface IReportingSink with
        member x.SinkName = "NBomber.Sinks.InfluxDB"

        member x.Init(context: IBaseContext, infraConfig: IConfiguration) =
            _logger <- context.Logger.ForContext<InfluxDBSink>()
            _context <- context

            infraConfig
            |> Option.ofObj
            |> Option.map(fun x -> x.GetSection("InfluxDBSink").Get<InfluxDbSinkConfig>())
            |> Option.bind(fun x ->
                if not(x |> box |> isNull) then Some x
                else None
            )
            |> Option.iter(fun config ->
                let metrics = MetricsBuilder().Report.ToInfluxDb(config.Url, config.DbName).Build()
                _metricsRoot <- Some metrics
            )

            Task.CompletedTask

        member x.Start() = Task.CompletedTask

        member x.SaveRealtimeStats(stats: ScenarioStats[]) =
            stats |> Array.iter(saveScenarioStats)
            Task.WhenAll(metricsRoot.ReportRunner.RunAllAsync())

        member x.SaveFinalStats(stats: NodeStats[]) = Task.CompletedTask
        member x.Stop() = Task.CompletedTask
        member x.Dispose() = ()
