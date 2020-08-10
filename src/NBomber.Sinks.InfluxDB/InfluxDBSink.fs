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

    let saveStepStats (operation: string,
                       scenarioName: string,
                       simulationStats: LoadSimulationStats,
                       nodeInfo: NodeInfo,
                       s: StepStats) =

        [("OkCount", float s.OkCount); ("FailCount", float s.FailCount);
         ("RPS", float s.RPS); ("Min", float s.Min); ("Mean", float s.Mean); ("Max", float s.Max)
         ("Percent50", float s.Percent50); ("Percent75", float s.Percent75); ("Percent95", float s.Percent95); ("Percent99", float s.Percent99); ("StdDev", float s.StdDev)
         ("MinDataKb", s.MinDataKb); ("MeanDataKb", s.MeanDataKb); ("MaxDataKb", s.MaxDataKb); ("AllDataMB", s.AllDataMB)
         ("LoadSimulationValue", float simulationStats.Value)]

        |> List.iter(fun (name, value) ->
            let metric =
                GaugeOptions(
                    Name = name,
                    Context = "NBomber",
                    Tags = MetricTags([|"node_type"; "test_suite"; "test_name"
                                        "scenario"; "step"; "operation"; "simulation"|],
                                      [|nodeInfo.NodeType.ToString(); _currentTestInfo.TestSuite; _currentTestInfo.TestName
                                        scenarioName; s.StepName; operation; simulationStats.SimulationName|]))
            metricsRoot.Measure.Gauge.SetValue(metric, value))

    let saveNodeStats (operation: string) (nodeStats: NodeStats) =
        nodeStats.ScenarioStats
        |> Seq.map(fun x -> x.ScenarioName, x.LoadSimulationStats, x.StepStats)
        |> Seq.iter(fun (name,simulationStats,stepStats) ->
            try
                stepStats
                |> Array.iter(fun stStats -> saveStepStats(operation,name,simulationStats,nodeStats.NodeInfo,stStats))
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
