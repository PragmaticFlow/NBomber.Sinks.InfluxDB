using System;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace NBomber.Sinks.InfluxDB.Examples.CSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            var influxDb = new InfluxDBSink(url: "http://localhost:8086", dbName: "default");

            var scenario = BuildScenario();
            NBomberRunner.RegisterScenarios(scenario)
                         .SaveStatisticsTo(influxDb)
                         .RunInConsole();
        }

        static Scenario BuildScenario()
        {
            var step = HttpStep.CreateRequest("GET", "https://www.youtube.com")                               
                               .WithHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8")
                               .WithHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.99 Safari/537.36")                               
                               .BuildStep("GET request");

            return ScenarioBuilder.CreateScenario("youtube_scenario", step)
                .WithConcurrentCopies(10)
                .WithDuration(TimeSpan.FromSeconds(10));
        }
    }
}
