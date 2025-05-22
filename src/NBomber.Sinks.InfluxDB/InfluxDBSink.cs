using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Configuration;
using Serilog;

using NBomber.Contracts;
using NBomber.Contracts.Metrics;
using NBomber.Contracts.Stats;

namespace NBomber.Sinks.InfluxDB
{
    /// <summary>
    /// Represents a custom key-value tag that can be attached to InfluxDB metrics.
    /// </summary>
    public class CustomTag
    {
        /// <summary>
        /// Gets or sets the tag key.
        /// </summary>
        public string Key { get; set; }
        
        /// <summary>
        /// Gets or sets the tag value.
        /// </summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// Represents the configuration settings for connecting to an InfluxDB instance.
    /// </summary>
    public class InfluxDbSinkConfig
    {
        /// <summary>
        /// Gets or sets the URL of the InfluxDB server.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the database name for InfluxDB 1.x compatibility.
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// Gets or sets the username for authentication (used in InfluxDB 1.x).
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password for authentication (used in InfluxDB 1.x).
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the token for authentication (used in InfluxDB 2.x).
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Gets or sets the organization name (used in InfluxDB 2.x).
        /// </summary>
        public string Org { get; set; }

        /// <summary>
        /// Gets or sets the bucket name (used in InfluxDB 2.x).
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Gets or sets an array of custom tags to include with each metric sent to InfluxDB.
        /// </summary>
        public CustomTag[] CustomTags { get; set; }
    }
    
    /// <summary>
    /// A reporting sink implementation for sending performance metrics to InfluxDB.
    /// </summary>
    public class InfluxDBSink : IReportingSink
    {
        private ILogger _logger;
        private IBaseContext _context;
        private InfluxDBClient _influxClient;
        private CustomTag[] _customTags = Array.Empty<CustomTag>();

        /// <summary>
        /// Gets the name of the reporting sink.
        /// </summary>
        public string SinkName => "NBomber.Sinks.InfluxDB";
        
        /// <summary>
        /// Gets the underlying <see cref="InfluxDBClient"/> used to write metrics.
        /// </summary>
        public InfluxDBClient InfluxClient => _influxClient;
        
        /// <summary>
        /// Gets the custom tags attached to every metric sent to InfluxDB.
        /// </summary>
        public CustomTag[] CustomTags => _customTags;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="InfluxDBSink"/> class with default settings.
        /// </summary>
        public InfluxDBSink()
        { }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="InfluxDBSink"/> class using an existing <see cref="InfluxDBClient"/> and optional custom tags.
        /// </summary>
        /// <param name="influxClient">The InfluxDB client used to send data.</param>
        /// <param name="customTags">Optional custom tags to include with each metric.</param>
        public InfluxDBSink(InfluxDBClient influxClient, CustomTag[] customTags = null)
        {
            _influxClient = influxClient;
            
            if (customTags != null)
                _customTags = customTags;
        }

        /// <summary>
        /// Initializes the reporting sink.
        /// This method is called before the test starts, and is typically used to read configuration settings and establishes a connection to reporting data storage.
        /// </summary>
        /// <param name="context">Provides access to NBomber's base execution context, including logger, node info, and test metadata.</param>
        /// <param name="infraConfig">Represents the infrastructure-specific JSON configuration.</param>
        public Task Init(IBaseContext context, IConfiguration infraConfig)
        {
            _logger = context.Logger.ForContext<InfluxDBSink>();
            _context = context;

            var config = infraConfig?.GetSection("InfluxDBSink").Get<InfluxDbSinkConfig>();
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.Database)) // Influx v1 
                {
                    _influxClient = new InfluxDBClient(
                        config.Url, config.UserName, config.Password, config.Database, retentionPolicy: "autogen"
                    );
                }
                else                                        // Influx v2
                {
                    var influxOpt = new InfluxDBClientOptions(config.Url);
                
                    if (!string.IsNullOrEmpty(config.UserName)) 
                        influxOpt.Username = config.UserName;
                
                    if (!string.IsNullOrEmpty(config.Password)) 
                        influxOpt.Password = config.Password;
                
                    if (!string.IsNullOrEmpty(config.Token))
                        influxOpt.Token = config.Token;
                
                    if (!string.IsNullOrEmpty(config.Org))
                        influxOpt.Org = config.Org;
                    
                    if (!string.IsNullOrEmpty(config.Bucket))
                        influxOpt.Bucket = config.Bucket;
                    
                    _influxClient = new InfluxDBClient(influxOpt);
                }

                if (config.CustomTags != null)
                    _customTags = config.CustomTags;
            }

            if (_influxClient == null)
            {
                _logger.Error("Reporting Sink {0} has problems with initialization. The problem could be related to invalid config structure.", SinkName);
                
                throw new Exception(
                    $"Reporting Sink {SinkName} has problems with initialization. The problem could be related to invalid config structure.");
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts the reporting sink at the beginning of a test session.
        /// This method is called at the start of the test and allows the sink to perform any necessary preparations before data collection begins.
        /// </summary>
        /// <param name="sessionInfo">Contains metadata about the test session and scenarios that will be executed.</param> 
        public async Task Start(SessionStartInfo sessionInfo)
        {
            var writeApi = _influxClient.GetWriteApiAsync();
            
            var point = PointData.Measurement("nbomber")
                .Field("cluster.node_count", 1)
                .Field("cluster.node_cpu_count", _context.GetNodeInfo().CoresCount);

            point = AddCustomTags(AddTestInfoTags(point, OperationType.Bombing));

            await writeApi.WritePointAsync(point);
        }
        
        /// <summary>
        /// Stops the reporting sink and releases any held resources (e.g., network or database connections).
        /// This method is invoked once the test session ends and should perform any necessary cleanup.
        /// </summary> 
        public Task Stop() => Task.CompletedTask;

        /// <summary>
        /// Saves real-time performance statistics during the test run.
        /// This method is invoked periodically based on the configured <c>ReportingInterval</c> to capture intermediate metrics.
        /// </summary>
        /// <param name="stats">Real-time stats data of the running scenarios.</param>
        public Task SaveRealtimeStats(ScenarioStats[] stats)
        {
            return SaveScenarioStats(stats, OperationType.Bombing);
        }

        /// <summary>
        /// Saves custom metrics collected during scenario execution.
        /// This method is invoked periodically based on the configured <c>ReportingInterval</c>,
        /// allowing the reporting sink to persist user-defined metrics such as counters, gauges, or other performance indicators.
        /// </summary>
        /// <param name="metrics">A collection of metrics captured during the test session.</param>
        /// <returns>A task that represents the asynchronous operation of saving the metrics.</returns>
        public async Task SaveRealtimeMetrics(MetricStats metrics)
        {
            var writeApi = _influxClient.GetWriteApiAsync();
            var counters = metrics.Counters.Select(x => MapCounter(x, OperationType.Bombing)).ToArray();
            var gauges = metrics.Gauges.Select(x => MapGauge(x, OperationType.Bombing)).ToArray();
            
            var writeCounters = writeApi.WritePointsAsync(counters);
            var writeGauges = writeApi.WritePointsAsync(gauges);
            
            await Task.WhenAll(writeCounters, writeGauges);
        }

        /// <summary>
        /// Saves final aggregated statistics after the test has completed.
        /// This method is called once at the end of the test session to persist final results.
        /// </summary>
        /// <param name="stats">The complete set of final statistics for all executed scenarios.</param>
        public Task SaveFinalStats(NodeStats stats)
        {
            return SaveScenarioStats(stats.ScenarioStats, OperationType.Complete);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _influxClient?.Dispose();
        }

        private PointData MapCounter(CounterStats counter, OperationType operationType)
        {
            var point = PointData.Measurement("nbomber")
                .Field($"counters.{counter.MetricName}", counter.Value);

            point = AddTestInfoTags(point, operationType);
            
            if (!string.IsNullOrEmpty(counter.ScenarioName))
                point = AddScenarioNameTag(point, counter.ScenarioName);
            
            return point;
        }
        
        private PointData MapGauge(GaugeStats gauge, OperationType operationType)
        {
            var point = PointData.Measurement("nbomber")
                .Field($"gauges.{gauge.MetricName}", gauge.Value);

            point = AddTestInfoTags(point, operationType);
            
            if (!string.IsNullOrEmpty(gauge.ScenarioName))
                point = AddScenarioNameTag(point, gauge.ScenarioName);
            
            return point;
        }
        
        private Task SaveScenarioStats(ScenarioStats[] stats, OperationType operationType)
        {   
            var writeApi = _influxClient.GetWriteApiAsync();
            var updatedStats = stats.Select(AddGlobalInfoStep).ToArray();
                
            var realtimeStats = updatedStats.SelectMany(x => MapStepsStats(x, operationType)).ToArray();
            var writeRealtimeStats = writeApi.WritePointsAsync(realtimeStats);
            
            var latencyCounts = stats.Select(x => MapLatencyCount(x, operationType)).ToArray();
            var writeLatencyCounts = writeApi.WritePointsAsync(latencyCounts);

            var statusCodes = stats.SelectMany(x => MapStatusCodes(x, operationType)).ToArray();
            var writeStatusCodes = writeApi.WritePointsAsync(statusCodes);

            return Task.WhenAll(writeRealtimeStats, writeLatencyCounts, writeStatusCodes);
        }

        private PointData AddTestInfoTags(PointData point, OperationType operationType)
        {
            var nodeInfo = _context.GetNodeInfo();
            var testInfo = _context.TestInfo;

            return point
                .Field("session_id", testInfo.SessionId)
                .Tag("current_operation", operationType.ToString().ToLower())
                .Tag("node_type", nodeInfo.NodeType.ToString())
                .Tag("test_suite", testInfo.TestSuite)
                .Tag("test_name", testInfo.TestName)
                .Tag("cluster_id", testInfo.ClusterId);
        }

        private PointData AddCustomTags(PointData point) => 
            _customTags.Aggregate(point, (current, t) => current.Tag(t.Key, t.Value));
        
        private PointData AddScenarioNameTag(PointData point, string scnName) => point.Tag("scenario", scnName);
        private PointData AddStepNameTag(PointData point, string stepName) => point.Tag("step", stepName);

        private ScenarioStats AddGlobalInfoStep(ScenarioStats scnStats)
        {
            var globalStepInfo = new StepStats("global information", scnStats.Ok, scnStats.Fail, sortIndex: 0);
            scnStats.StepStats = scnStats.StepStats.Append(globalStepInfo).ToArray();
            
            return scnStats;
        }
        
        private IEnumerable<PointData> MapStepsStats(ScenarioStats scnStats, OperationType operationType)
        {
            var simulation = scnStats.LoadSimulationStats;
            
            return scnStats.StepStats.Select(step =>
            {
                var okR = step.Ok.Request;
                var okL = step.Ok.Latency;
                var okD = step.Ok.DataTransfer;

                var fR = step.Fail.Request;
                var fL = step.Fail.Latency;
                var fD = step.Fail.DataTransfer;

                var point = PointData.Measurement("nbomber")
                    .Field("all.request.count", step.Ok.Request.Count + step.Fail.Request.Count)
                    .Field("all.datatransfer.all", step.Ok.DataTransfer.AllBytes + step.Fail.DataTransfer.AllBytes)
                    
                    // OK
                    .Field("ok.request.count", okR.Count)
                    .Field("ok.request.rps", okR.RPS)
                    
                    .Field("ok.latency.min", okL.MinMs)
                    .Field("ok.latency.mean", okL.MeanMs)
                    .Field("ok.latency.max", okL.MaxMs)
                    .Field("ok.latency.stddev", okL.StdDev)
                    .Field("ok.latency.percent50", okL.Percent50)
                    .Field("ok.latency.percent75", okL.Percent75)
                    .Field("ok.latency.percent95", okL.Percent95)
                    .Field("ok.latency.percent99", okL.Percent99)
                    
                    .Field("ok.datatransfer.min", okD.MinBytes)
                    .Field("ok.datatransfer.mean", okD.MeanBytes)
                    .Field("ok.datatransfer.max", okD.MaxBytes)
                    .Field("ok.datatransfer.all", okD.AllBytes)
                    .Field("ok.datatransfer.percent50", okD.Percent50)
                    .Field("ok.datatransfer.percent75", okD.Percent75)
                    .Field("ok.datatransfer.percent95", okD.Percent95)
                    .Field("ok.datatransfer.percent99", okD.Percent99)
                    
                    // FAIL
                    .Field("fail.request.count", fR.Count)
                    .Field("fail.request.rps", fR.RPS)
                    
                    .Field("fail.latency.min", fL.MinMs)
                    .Field("fail.latency.mean", fL.MeanMs)
                    .Field("fail.latency.max", fL.MaxMs)
                    .Field("fail.latency.stddev", fL.StdDev)
                    .Field("fail.latency.percent50", fL.Percent50)
                    .Field("fail.latency.percent75", fL.Percent75)
                    .Field("fail.latency.percent95", fL.Percent95)
                    .Field("fail.latency.percent99", fL.Percent99)
                    
                    .Field("fail.datatransfer.min", fD.MinBytes)
                    .Field("fail.datatransfer.mean", fD.MeanBytes)
                    .Field("fail.datatransfer.max", fD.MaxBytes)
                    .Field("fail.datatransfer.all", fD.AllBytes)
                    .Field("fail.datatransfer.percent50", fD.Percent50)
                    .Field("fail.datatransfer.percent75", fD.Percent75)
                    .Field("fail.datatransfer.percent95", fD.Percent95)
                    .Field("fail.datatransfer.percent99", fD.Percent99)
                    
                    .Field("simulation.value", simulation.Value);

                point = AddCustomTags(AddTestInfoTags(point, operationType));
                point = AddStepNameTag(point, step.StepName);
                point = AddScenarioNameTag(point, scnStats.ScenarioName);

                return point;
            });
        }

        private PointData MapLatencyCount(ScenarioStats scnStats, OperationType operationType)
        {
            var point = PointData
                .Measurement("nbomber")
                .Field("latency_count.less_or_eq_800", scnStats.Ok.Latency.LatencyCount.LessOrEq800)
                .Field("latency_count.more_800_less_1200", scnStats.Ok.Latency.LatencyCount.More800Less1200)
                .Field("latency_count.more_or_eq_1200", scnStats.Ok.Latency.LatencyCount.MoreOrEq1200);

            point = AddCustomTags(AddTestInfoTags(point, operationType));
            point = AddScenarioNameTag(point, scnStats.ScenarioName);

            return point;
        }

        private IEnumerable<PointData> MapStatusCodes(ScenarioStats scnStats, OperationType operationType)
        {
            return scnStats
                .Ok.StatusCodes.Concat(scnStats.Fail.StatusCodes)
                .Select(s =>
                {
                    var point = PointData
                        .Measurement("nbomber")
                        .Tag("status_code.status", s.StatusCode)
                        .Field("status_code.count", s.Count);

                    point = AddCustomTags(AddTestInfoTags(point, operationType));
                    point = AddScenarioNameTag(point, scnStats.ScenarioName);

                    return point;
                });
        }
    }
}