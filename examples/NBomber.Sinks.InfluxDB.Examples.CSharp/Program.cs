namespace NBomber.Sinks.InfluxDB.Examples.CSharp
{
    using System;
    using NBomber.CSharp;
    using NBomber.Http.CSharp;

    class Program
    {
        static void Main(string[] args)
        {
            var influxDb = new InfluxDBSink(url: "http://localhost:8086", dbName: "NBomberDb");

            var step = HttpStep.Create("simple step", (context) =>
                    Http.CreateRequest("GET", "https://nbomber.com")
            );

            var scenario = ScenarioBuilder.CreateScenario("test_nbomber", new[] { step })
                .WithConcurrentCopies(100)
                .WithWarmUpDuration(TimeSpan.FromSeconds(10))
                .WithDuration(TimeSpan.FromMinutes(3));

            NBomberRunner.RegisterScenarios(scenario)
                         .WithReportingSinks(new[] { influxDb }, TimeSpan.FromSeconds(20))
                         .RunInConsole();
        }
    }
}
