[![Build status](https://ci.appveyor.com/api/projects/status/ptahoo3renvkn7vu?svg=true)](https://ci.appveyor.com/project/PragmaticFlowOrg/nbomber-sinks-influxdb)
[![NuGet](https://img.shields.io/nuget/v/nbomber.sinks.influxdb.svg)](https://www.nuget.org/packages/nbomber.sinks.influxdb/)
[![Gitter](https://badges.gitter.im/nbomber/community.svg)](https://gitter.im/nbomber/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)

A NBomber sink that writes statistics to InfluxDB

### How to install
To install NBomber.Sinks.InfluxDB via NuGet, run this command in NuGet package manager console:
```code
PM> Install-Package NBomber.Sinks.InfluxDB
```

### Documentation
Documentation is located [here](https://nbomber.com).

### Contributing
Would you like to help make NBomber even better? We keep a list of issues that are approachable for newcomers under the [good-first-issue](https://github.com/PragmaticFlow/NBomber.Sinks.InfluxDB/issues?q=is%3Aopen+is%3Aissue+label%3A%22good+first+issue%22) label.

### Examples
```csharp
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
```
