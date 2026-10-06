using InfluxDB.Client;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB.Tests.Infra;
using Shouldly;

namespace NBomber.Sinks.InfluxDB.Tests;

public class InfluxDBSinkTests(InfluxDbFixture fixture) : IClassFixture<InfluxDbFixture>
{
    private const string ScenarioName = "e2e_scenario";
    private const string StepName = "step_1";
    private const string CounterMetricName = "e2e-custom-counter";
    private const string GaugeMetricName = "e2e-custom-gauge";
    private const double GaugeValue = 42.5;

    [Fact]
    public async Task ReportingSink_should_write_final_scenario_stats()
    {
        var testName = CreateTestName();
        var sink = CreateSink();

        var stats = RunLoadTest(sink, testName);

        var scnStats = stats.ScenarioStats.Get(ScenarioName);
        var stepStats = scnStats.StepStats.First(x => x.StepName == StepName);
        scnStats.Ok.Request.Count.ShouldBeGreaterThan(0);

        var scenarioTags = GenerateTags(testName, OperationType.Complete, new() { ["step"] = "global information" });
        var stepTags = GenerateTags(testName, OperationType.Complete, new() { ["step"] = StepName });

        var scnOkCount = await fixture.DbReader.WaitForField("ok.request.count", scenarioTags);
        scnOkCount.ShouldBe(scnStats.Ok.Request.Count);

        var scnFailCount = await fixture.DbReader.WaitForField("fail.request.count", scenarioTags);
        scnFailCount.ShouldBe(scnStats.Fail.Request.Count);

        var stepOkCount = await fixture.DbReader.WaitForField("ok.request.count", stepTags);
        stepOkCount.ShouldBe(stepStats.Ok.Request.Count);

        var stepLatencyMax = await fixture.DbReader.WaitForField("ok.latency.max", stepTags);
        stepLatencyMax.ShouldBe(stepStats.Ok.Latency.MaxMs, tolerance: 0.001);

        var latencyCountTags = GenerateTags(testName, OperationType.Complete);
        var scnLatencyCount = await fixture.DbReader.WaitForField("latency_count.less_or_eq_800", latencyCountTags);
        scnLatencyCount.ShouldBe(scnStats.Ok.Latency.LatencyCount.LessOrEq800);

        var statusCodeTags = GenerateTags(testName, OperationType.Complete, new() { ["status_code.status"] = "200" });
        var statusCodeCount = await fixture.DbReader.WaitForField("status_code.count", statusCodeTags);
        statusCodeCount.ShouldBe(scnStats.Ok.StatusCodes.First(x => x.StatusCode == "200").Count);
    }

    [Fact]
    public async Task ReportingSink_should_write_realtime_stats()
    {
        var testName = CreateTestName();
        var sink = CreateSink();

        RunLoadTest(sink, testName);

        var tags = GenerateTags(testName, OperationType.Bombing, new() { ["step"] = StepName });

        var okCount = await fixture.DbReader.WaitForField("ok.request.count", tags);
        okCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ReportingSink_should_write_custom_metrics()
    {
        var testName = CreateTestName();
        var sink = CreateSink();

        var stats = RunLoadTest(sink, testName);

        var counterStats = stats.Metrics.Counters.First(x => x.MetricName == CounterMetricName);
        var gaugeStats = stats.Metrics.Gauges.First(x => x.MetricName == GaugeMetricName);
        counterStats.Value.ShouldBeGreaterThan(0);

        var tags = GenerateTags(testName, OperationType.Complete);

        var counter = await fixture.DbReader.WaitForField($"counters.{CounterMetricName}", tags);
        counter.ShouldBe(counterStats.Value);

        var gauge = await fixture.DbReader.WaitForField($"gauges.{GaugeMetricName}", tags);
        gaugeStats.Value.ShouldBe(GaugeValue);
        gauge.ShouldBe(gaugeStats.Value);
    }

    private static NodeStats RunLoadTest(InfluxDBSink sink, string testName)
    {
        var counter = Metric.CreateCounter(CounterMetricName, unitOfMeasure: "MB");
        var gauge = Metric.CreateGauge(GaugeMetricName, unitOfMeasure: "KB");

        var scenario = Scenario.Create(ScenarioName, async context =>
        {
            await Step.Run(StepName, context, async () =>
            {
                await Task.Delay(10);

                counter.Add(1);
                gauge.Set(GaugeValue);

                return Response.Ok(statusCode: "200", sizeBytes: 100);
            });

            return Response.Ok();
        })
        .WithInit(ctx =>
        {
            ctx.RegisterMetric(counter);
            ctx.RegisterMetric(gauge);
            return Task.CompletedTask;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // longer than the reporting interval, so at least one realtime report is sent
            Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(7))
        );

        return NBomberRunner
            .RegisterScenarios(scenario)
            .WithTestSuite("e2e")
            .WithTestName(testName)
            .WithReportingInterval(TimeSpan.FromSeconds(5))
            .WithoutReports()
            .WithReportingSinks(sink)
            .Run();
    }

    private InfluxDBSink CreateSink()
    {
        var options = new InfluxDBClientOptions(fixture.Url)
        {
            Org = fixture.Org,
            Bucket = fixture.Database
        };

        return new InfluxDBSink(new InfluxDBClient(options));
    }

    private static string CreateTestName() => $"influx_{Guid.NewGuid():N}";

    private static Dictionary<string, string> GenerateTags(string testName, OperationType operationType,
        Dictionary<string, string>? customTags = null)
    {
        var tags = new Dictionary<string, string>
        {
            ["test_suite"] = "e2e",
            ["test_name"] = testName,
            ["scenario"] = ScenarioName,
            ["current_operation"] = operationType.ToString().ToLower()
        };

        if (customTags != null)
        {
            foreach (var (key, value) in customTags)
                tags[key] = value;
        }

        return tags;
    }
}