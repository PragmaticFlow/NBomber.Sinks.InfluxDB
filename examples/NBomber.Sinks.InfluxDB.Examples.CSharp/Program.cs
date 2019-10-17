namespace NBomber.Sinks.InfluxDB.Examples.CSharp
{
    using System;
    using NBomber.CSharp;
    using NBomber.Http.CSharp;
    
    class Program
    {
        static void Main(string[] args)
        {
            var influxDb = new InfluxDBSink(url: "http://localhost:8086", dbName: "default");
            
            var step = HttpStep.Create("simple step", (context) =>
                    Http.CreateRequest("GET", "https://gitter.im")
                        .WithHeader("Accept", "text/html")
            );
            
            var scenario = ScenarioBuilder.CreateScenario("test_gitter", step)
                .WithConcurrentCopies(100)
                .WithWarmUpDuration(TimeSpan.FromSeconds(40))
                .WithDuration(TimeSpan.FromSeconds(60));
            
            NBomberRunner.RegisterScenarios(scenario)
                         .SaveStatisticsTo(influxDb)
                         .RunInConsole();
        }
    }
}
