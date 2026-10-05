using System.Globalization;
using InfluxDB3.Client;
using InfluxDB3.Client.Config;

namespace NBomber.Sinks.InfluxDB.Tests.Infra.Helpers;

/// <summary>
/// Reads the data written by the sink from InfluxDB 3 using SQL queries.
/// </summary>
public class InfluxDbReader(string url, string database) : IDisposable
{
    private const string Measurement = "nbomber";

    private readonly InfluxDBClient _client = new(new ClientConfig { Host = url, Database = database });

    /// <summary>
    /// Polls InfluxDB until the field with the given tags appears and returns its latest value.
    /// </summary>
    public async Task<double> WaitForField(string field, IReadOnlyDictionary<string, string> tags, TimeSpan? timeout = null)
    {
        var sql = BuildQuery(field, tags);
        var parameters = tags.ToDictionary(tag => ToParameterName(tag.Key), object (tag) => tag.Value);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));

        while (true)
        {
            await foreach (var row in _client.Query(sql, namedParameters: parameters))
                return Convert.ToDouble(row[0], CultureInfo.InvariantCulture);

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Query '{sql}' returned no data from InfluxDB.");

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public void Dispose() => _client.Dispose();

    // e.g. SELECT "ok.request.count" FROM nbomber
    //      WHERE "ok.request.count" IS NOT NULL AND "step" = $step
    //      ORDER BY time DESC LIMIT 1
    private static string BuildQuery(string field, IReadOnlyDictionary<string, string> tags)
    {
        var conditions = tags.Keys
            .Select(tag => $"\"{tag}\" = ${ToParameterName(tag)}")
            .Prepend($"\"{field}\" IS NOT NULL");

        return $"SELECT \"{field}\" FROM {Measurement} " +
               $"WHERE {string.Join(" AND ", conditions)} " +
               "ORDER BY time DESC LIMIT 1";
    }

    // tag names may contain dots (e.g. "status_code.status"), which are not allowed in parameter names
    private static string ToParameterName(string tag) => tag.Replace('.', '_');
}
