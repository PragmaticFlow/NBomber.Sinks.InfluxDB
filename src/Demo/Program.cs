using AutoBogus;
using InfluxDB.Client.Writes;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB;
using Newtonsoft.Json;
using Serilog;
using TimeSpan = System.TimeSpan;

new InfluxDBReportingExample().Run();

public class InfluxDBReportingExample
{
    private readonly InfluxDBSink _influxDbSink = new();

    private ScenarioStats[] _stats;
    
    public void Run()
    {
        var writeScenario = Scenario.Create("write_scenario", async context =>
            {
                await _influxDbSink.SaveRealtimeStats(_stats);
                
                return Response.Ok(statusCode: "201");
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.KeepConstant(50, TimeSpan.FromSeconds(30))
            ).WithInit(context =>
            {
                _stats = new ScenarioStats[2];

                var faker = AutoFaker.Create();
                for (var i = 0; i < _stats.Length; i++)
                {
                    _stats[i] = faker.Generate<ScenarioStats>();
                    _stats[i].StepStats =
                        [faker.Generate<StepStats>(), faker.Generate<StepStats>(), faker.Generate<StepStats>()];
                }
                
                return Task.CompletedTask;
            });

        // var readScenario = Scenario.Create("read_scenario", async context =>
        //     {
        //         var query = @"
        //             from(bucket: ""nbomber"")
        //             |> range(start: -1m)
        //             ";
        //         
        //         var readApi = _influxDbSink.InfluxClient.GetQueryApi();
        //         var results = await readApi.QueryAsync(query, "nbomber");
        //
        //         var serializedResults = JsonConvert.SerializeObject(results);
        //         if (results is null || string.IsNullOrEmpty(serializedResults))
        //         {
        //             context.Logger.Information("no data");
        //         }
        //         else
        //         {
        //             context.Logger.Information($"Data from influxdb: {serializedResults}");
        //         }
        //     
        //         return Response.Ok(statusCode: "200");
        // })
        // .WithoutWarmUp()
        // .WithLoadSimulations(
        //     Simulation.KeepConstant(20, TimeSpan.FromSeconds(30))
        // );
        
        NBomberRunner
            .RegisterScenarios(writeScenario)
            .LoadInfraConfig("infra-config.json")
            //.WithReportingInterval(TimeSpan.FromSeconds(5))
            .WithReportingSinks(_influxDbSink)
            .WithTestSuite("reporting")
            .WithTestName("influx_db_demo")
            .Run();
    }
}