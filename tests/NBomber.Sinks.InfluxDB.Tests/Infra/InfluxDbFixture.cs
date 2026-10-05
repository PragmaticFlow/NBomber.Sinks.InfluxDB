using NBomber.Sinks.InfluxDB.Tests.Infra.Helpers;

namespace NBomber.Sinks.InfluxDB.Tests.Infra;

/// <summary>
/// Points tests to InfluxDB 3 Core started from Docker/docker-compose.yaml:
/// <code>docker compose up -d --wait</code>
/// </summary>
public class InfluxDbFixture : IDisposable
{
    public string Url => "http://localhost:8181";

    // InfluxDB 3 ignores the org; the v2 write API bucket is the database name
    public string Org => "nbomber";
    public string Database => "nbomber";

    public InfluxDbReader DbReader { get; }

    public InfluxDbFixture()
    {
        DbReader = new InfluxDbReader(Url, Database);
    }

    public void Dispose() => DbReader.Dispose();
}
