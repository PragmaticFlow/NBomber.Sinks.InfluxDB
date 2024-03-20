using InfluxDB.Client.Writes;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB;
using TimeSpan = System.TimeSpan;

new InfluxDBReportingExample().Run();

public class InfluxDBReportingExample
{
    private readonly InfluxDBSink _influxDbSink = new();

    public void Run()
    {
        var writeScenario = Scenario.Create("write_scenario", async context =>
            {
                var writeApi = _influxDbSink.InfluxClient.GetWriteApiAsync();
                
                var point = PointData
                    .Measurement("nbomber")
                    .Field("my_custom_counter", 1);
                
                await writeApi.WritePointsAsync(Enumerable.Repeat(point, 5).ToArray());
                
                return Response.Ok(statusCode: "201");
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.KeepConstant(20, TimeSpan.FromSeconds(30))
            );

        var readScenario = Scenario.Create("read_scenario", async context =>
            {
                var query = @"
                    from(bucket: ""nbomber"")
                    |> range(start: -1m)
                    ";
                
                var readApi = _influxDbSink.InfluxClient.GetQueryApi();
                var results = await readApi.QueryAsync(query, "nbomber");
            
            return Response.Ok(statusCode: "200");
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.KeepConstant(20, TimeSpan.FromSeconds(30))
        );
        
        NBomberRunner
            .RegisterScenarios(writeScenario, readScenario)
            .LoadInfraConfig("infra-config.json")
            //.WithReportingInterval(TimeSpan.FromSeconds(5))
            .WithReportingSinks(_influxDbSink)
            .WithTestSuite("reporting")
            .WithTestName("influx_db_demo")
            .Run();
    }
}