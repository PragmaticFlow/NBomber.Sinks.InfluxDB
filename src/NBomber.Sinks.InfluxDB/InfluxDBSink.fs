namespace NBomber.Sinks.InfluxDB

open System.Threading.Tasks

open Serilog
open App.Metrics
open App.Metrics.Gauge
open Microsoft.Extensions.Configuration

open NBomber.Contracts

type InfluxDBSink(metricsRoot: IMetricsRoot) =

    let mutable _logger = Unchecked.defaultof<ILogger>
    let mutable _currentTestInfo = Unchecked.defaultof<TestInfo>

    let saveStepStats (operation: string, scenarioName: string, nodeInfo: NodeInfo, s: StepStats) =
        [("OkCount", float s.OkCount); ("FailCount", float s.FailCount);
         ("RPS", float s.RPS); ("Min", float s.Min); ("Mean", float s.Mean); ("Max", float s.Max)
         ("Percent50", float s.Percent50); ("Percent75", float s.Percent75); ("Percent95", float s.Percent95); ("StdDev", float s.StdDev)
         ("MinDataKb", s.MinDataKb); ("MeanDataKb", s.MeanDataKb); ("MaxDataKb", s.MaxDataKb); ("AllDataMB", s.AllDataMB)]

        |> List.iter(fun (name, value) ->
            let metric =
                GaugeOptions(
                    Name = name,
                    Context = "NBomber",
                    Tags = MetricTags([|"machine_name"; "node_type"
                                        "session_id"; "test_suite"; "test_name"
                                        "scenario"; "step"; "operation"|],
                                      [|nodeInfo.MachineName; nodeInfo.NodeType.ToString()
                                        _currentTestInfo.SessionId; _currentTestInfo.TestSuite; _currentTestInfo.TestName
                                        scenarioName; s.StepName; operation |]))
            metricsRoot.Measure.Gauge.SetValue(metric, value))

    let saveNodeStats (operation: string) (nodeStats: NodeStats) =
        nodeStats.ScenarioStats
        |> Array.map(fun x -> x.ScenarioName, x.StepStats)
        |> Array.iter(fun (name,stats) ->
            try
                stats |> Array.iter(fun x -> saveStepStats(operation,name,nodeStats.NodeInfo,x))
            with
            | ex -> _logger.Error(ex.ToString())
        )

    new (url: string, dbName: string) =
        let metrics = MetricsBuilder().Report.ToInfluxDb(url, dbName).Build()
        new InfluxDBSink(metrics)

    interface IReportingSink with
        member x.SinkName = "NBomber.Sinks.InfluxDB"

        member x.Init(logger: ILogger, infraConfig: IConfiguration option) =
            _logger <- logger.ForContext<InfluxDBSink>()

        member x.StartTest(testInfo: TestInfo) =
            _currentTestInfo <- testInfo
            Task.CompletedTask

        member x.SaveRealtimeStats (stats: NodeStats[]) =
            stats |> Array.iter(saveNodeStats "bombing")
            Task.WhenAll(metricsRoot.ReportRunner.RunAllAsync())

        member x.SaveFinalStats(stats: NodeStats[]) =
            stats |> Array.iter(saveNodeStats "complete")
            Task.CompletedTask

        member x.StopTest() =
            Task.CompletedTask

        member x.Dispose() = ()
