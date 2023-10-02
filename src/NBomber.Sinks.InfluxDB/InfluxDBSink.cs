using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Configuration;
using Serilog;

using NBomber.Contracts;
using NBomber.Contracts.Stats;

namespace NBomber.Sinks.InfluxDB
{
    public class CustomTag
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class InfluxDbSinkConfig
    {
        public string Url { get; set; }
        public string Database { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Token { get; set; }
        public string Org { get; set; }
        public string Bucket { get; set; }
        public CustomTag[] CustomTags { get; set; }
    }
    
    /// <summary>
    /// This class represent ReportingSink for InfluxDB.
    /// It supports to work with InfluxDB v1.8 and InfluxDB v2.0.
    /// </summary>
    public class InfluxDBSink : IReportingSink
    {
        private ILogger _logger;
        private IBaseContext _context;
        private InfluxDBClient _influxClient;
        private CustomTag[] _customTags = Array.Empty<CustomTag>();

        public string SinkName => "NBomber.Sinks.InfluxDB";
        public InfluxDBClient InfluxClient => _influxClient;
        public CustomTag[] CustomTags => _customTags;
        
        public InfluxDBSink()
        { }
        
        public InfluxDBSink(InfluxDBClient influxClient, CustomTag[] customTags = null)
        {
            _influxClient = influxClient;
            
            if (customTags != null)
                _customTags = customTags;
        }

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
                
                    // mandatory params
                    influxOpt.Org = config.Org;
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

        public async Task Start()
        {
            if (_influxClient != null)
            {
                var writeApi = _influxClient.GetWriteApiAsync();
                
                var point = PointData.Measurement("nbomber")
                    .Field("cluster.node_count", 1)
                    .Field("cluster.node_cpu_count", _context.GetNodeInfo().CoresCount);

                point = AddCustomTags(AddTestInfoTags(point));

                await writeApi.WritePointAsync(point);
            }
        }
        
        public Task Stop() => Task.CompletedTask;

        public Task SaveRealtimeStats(ScenarioStats[] stats)
        {
            return SaveScenarioStats(stats);
        }

        public Task SaveFinalStats(NodeStats stats)
        {
            return SaveScenarioStats(stats.ScenarioStats);
        }

        public void Dispose()
        {
            _influxClient?.Dispose();
        }
        
        Task SaveScenarioStats(ScenarioStats[] stats)
        {
            if (_influxClient != null)
            {
                var writeApi = _influxClient.GetWriteApiAsync();
                var updatedStats = stats.Select(AddGlobalInfoStep).ToArray();
                    
                var realtimeStats = updatedStats.SelectMany(MapStepsStats).ToArray();
                var writeRealtimeStats = writeApi.WritePointsAsync(realtimeStats);
                
                var latencyCounts = stats.Select(MapLatencyCount).ToArray();
                var writeLatencyCounts = writeApi.WritePointsAsync(latencyCounts);

                var statusCodes = stats.SelectMany(MapStatusCodes).ToArray();
                var writeStatusCodes = writeApi.WritePointsAsync(statusCodes);

                return Task.WhenAll(writeRealtimeStats, writeLatencyCounts, writeStatusCodes);
            }

            return Task.CompletedTask;
        }

        PointData AddTestInfoTags(PointData point)
        {
            var nodeInfo = _context.GetNodeInfo();
            var testInfo = _context.TestInfo;

            return point
                .Field("session_id", testInfo.SessionId)
                .Tag("current_operation", nodeInfo.CurrentOperation.ToString().ToLower())
                .Tag("node_type", nodeInfo.NodeType.ToString())
                .Tag("test_suite", testInfo.TestSuite)
                .Tag("test_name", testInfo.TestName)
                .Tag("cluster_id", testInfo.ClusterId);
        }

        PointData AddCustomTags(PointData point) => 
            _customTags.Aggregate(point, (current, t) => current.Tag(t.Key, t.Value));
        
        PointData AddScenarioNameTag(PointData point, string scnName) => point.Tag("scenario", scnName);
        PointData AddStepNameTag(PointData point, string stepName) => point.Tag("step", stepName);

        ScenarioStats AddGlobalInfoStep(ScenarioStats scnStats)
        {
            var globalStepInfo = new StepStats("global information", scnStats.Ok, scnStats.Fail);
            scnStats.StepStats = scnStats.StepStats.Append(globalStepInfo).ToArray();
            
            return scnStats;
        }
        
        IEnumerable<PointData> MapStepsStats(ScenarioStats scnStats)
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

                point = AddCustomTags(AddTestInfoTags(point));
                point = AddStepNameTag(point, step.StepName);
                point = AddScenarioNameTag(point, scnStats.ScenarioName);

                return point;
            });
        }

        PointData MapLatencyCount(ScenarioStats scnStats)
        {
            var point = PointData
                .Measurement("nbomber")
                .Field("latency_count.less_or_eq_800", scnStats.Ok.Latency.LatencyCount.LessOrEq800)
                .Field("latency_count.more_800_less_1200", scnStats.Ok.Latency.LatencyCount.More800Less1200)
                .Field("latency_count.more_or_eq_1200", scnStats.Ok.Latency.LatencyCount.MoreOrEq1200);

            point = AddCustomTags(AddTestInfoTags(point));
            point = AddScenarioNameTag(point, scnStats.ScenarioName);

            return point;
        }

        IEnumerable<PointData> MapStatusCodes(ScenarioStats scnStats)
        {
            return scnStats
                .Ok.StatusCodes.Concat(scnStats.Fail.StatusCodes)
                .Select(s =>
                {
                    var point = PointData
                        .Measurement("nbomber")
                        .Tag("status_code.status", s.StatusCode)
                        .Field("status_code.count", s.Count);

                    point = AddCustomTags(AddTestInfoTags(point));
                    point = AddScenarioNameTag(point, scnStats.ScenarioName);

                    return point;
                });
        }
    }
}