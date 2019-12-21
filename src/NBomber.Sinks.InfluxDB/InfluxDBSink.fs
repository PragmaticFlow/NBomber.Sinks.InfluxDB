namespace NBomber.Sinks.InfluxDB

open System.Threading.Tasks
open App.Metrics
open App.Metrics.Gauge
open NBomber.Contracts

type InfluxDBSink(url: string, dbName: string) = 

    let metrics = MetricsBuilder().Report.ToInfluxDb(url, dbName).Build()

    let saveGaugeMetrics (testInfo: TestInfo) (s: Statistics) =
        
        let operation =
            match s.NodeInfo.CurrentOperation with            
            | NodeOperationType.Bombing  -> "bombing"
            | NodeOperationType.Complete -> "complete"
            | _                          -> "unknown_operation"
            
        [("OkCount", float s.OkCount); ("FailCount", float s.FailCount); 
         ("RPS", float s.RPS); ("Min", float s.Min); ("Mean", float s.Mean); ("Max", float s.Max)            
         ("Percent50", float s.Percent50); ("Percent75", float s.Percent75); ("Percent95", float s.Percent95); ("StdDev", float s.StdDev)                 
         ("DataMinKb", s.DataMinKb); ("DataMeanKb", s.DataMeanKb); ("DataMaxKb", s.DataMaxKb); ("AllDataMB", s.AllDataMB)]
        
        |> List.iter(fun (name, value) -> 
            let m = GaugeOptions(
                        Name = name,
                        Context = "NBomber",
                        Tags = MetricTags([|"machineName"; "sender"
                                            "session_id"; "test_suite"; "test_name"
                                            "scenario"; "step"; "operation"|],
                                          [|s.NodeInfo.MachineName; s.NodeInfo.Sender.ToString()
                                            testInfo.SessionId; testInfo.TestSuite; testInfo.TestName
                                            s.ScenarioName; s.StepName; operation |]))
            metrics.Measure.Gauge.SetValue(m, value))

    interface IReportingSink with
        member x.StartTest(testInfo: TestInfo) =
            Task.CompletedTask
        
        member x.SaveRealtimeStats (testInfo: TestInfo, stats: Statistics[]) =        
            stats |> Array.iter(saveGaugeMetrics testInfo)
            Task.WhenAll(metrics.ReportRunner.RunAllAsync())
            
        member x.SaveFinalStats(testInfo: TestInfo, stats: Statistics[], reportFiles: ReportFile[]) =
            stats |> Array.iter(saveGaugeMetrics testInfo)
            Task.CompletedTask
            
        member x.FinishTest(testInfo: TestInfo) =
            Task.CompletedTask