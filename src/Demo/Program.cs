using AutoBogus;
using Demo;
using InfluxDB.Client;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB;
using TimeSpan = System.TimeSpan;

new InfluxDBReportingExample().Run();

public class InfluxDBReportingExample
{
    //private readonly InfluxDBSink _influxDbSink = new();

    private readonly PointSimulation _pointSimulation;

    private readonly InfluxDBClient _influxDbClient;

    private ScenarioStats[] _stats;

    public InfluxDBReportingExample()
    {
        var influxOpt = new InfluxDBClientOptions("http://localhost:8086")
        {
            Username = "admin",
            Password = "adminadmin",
            Token = "secret-token",
            Bucket = "nbomber",
            Org = "nbomber"
        };

        _influxDbClient = new InfluxDBClient(influxOpt);
            
        _pointSimulation = new PointSimulation(_influxDbClient);
    }
    
    public void Run()
    {
        var writeScenario1 = Scenario.Create("write_scenario_1", async context =>
            {
                await _pointSimulation.SaveRealtimeStats(_stats, context.ScenarioInfo.ThreadNumber.ToString());
                
                await Task.Delay(TimeSpan.FromSeconds(5));
                
                return Response.Ok(statusCode: "201");
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.KeepConstant(1, TimeSpan.FromSeconds(30))
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

        var readScenario1 = Scenario.Create("read_scenario_1", async context =>
            {
                var query = $@"
                    from(bucket: ""nbomber"")
                    |> range(start: -1m)
                    |> filter(fn: (r) => r[""_field""] == ""session_id"")
                    |> filter(fn: (r) => r[""_value""] == ""{context.ScenarioInfo.ThreadNumber}"")
                    ";
                
                var readApi = _influxDbClient.GetQueryApi();
                var content = await readApi.QueryAsync(query, "nbomber");
    
                await Task.Delay(TimeSpan.FromSeconds(1));
                
                return Response.Ok(statusCode: "200");
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.KeepConstant(1, TimeSpan.FromSeconds(30))
        );
        
        NBomberRunner
            .RegisterScenarios(writeScenario1, readScenario1)
            .LoadInfraConfig("infra-config.json")
            /*.WithReportingInterval(TimeSpan.FromSeconds(5))
            .WithReportingSinks(_influxDbSink)*/
            .WithTestSuite("reporting")
            .WithTestName("influx_db_demo")
            .Run();
    }
}