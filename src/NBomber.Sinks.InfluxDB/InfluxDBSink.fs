namespace NBomber.Sinks.InfluxDB

open System.Threading.Tasks
open App.Metrics
open App.Metrics.Gauge
open NBomber.Contracts

type InfluxDBSink(url: string, dbName: string) = 

    let metrics = MetricsBuilder().Report.ToInfluxDb(url, dbName).Build()

    let saveGaugeMetrics (s: Statistics) =
        let contextName = sprintf "%s_%s" s.ScenarioName s.StepName

        [("OkCount", float s.OkCount); ("FailCount", float s.FailCount); 
         ("RPS", float s.RPS); ("Min", float s.Min); ("Mean", float s.Mean); ("Max", float s.Max)            
         ("Percent50", float s.Percent50); ("Percent75", float s.Percent75); ("Percent95", float s.Percent95); ("StdDev", float s.StdDev)                 
         ("DataMinKb", s.DataMinKb); ("DataMeanKb", s.DataMeanKb); ("DataMaxKb", s.DataMaxKb); ("AllDataMB", s.AllDataMB)]        

        |> List.iter(fun (name, value) -> 
            let m = GaugeOptions(
                        Name = name,
                        Context = contextName, 
                        Tags = MetricTags([|"machineName"; "sender"|], 
                                          [|s.Meta.MachineName; s.Meta.Sender.ToString()|]))
            metrics.Measure.Gauge.SetValue(m, value))

    interface IStatisticsSink with
        member x.SaveStatistics (statistics: Statistics[]) =        
            statistics |> Array.iter(saveGaugeMetrics)
            Task.WhenAll(metrics.ReportRunner.RunAllAsync())  
