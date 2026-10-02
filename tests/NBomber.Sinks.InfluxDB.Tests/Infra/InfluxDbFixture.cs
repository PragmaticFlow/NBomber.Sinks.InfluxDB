namespace NBomber.Sinks.InfluxDB.Tests;

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

    public InfluxDbReader Reader { get; }

    public InfluxDbFixture()
    {
        Reader = new InfluxDbReader(Url, Database);
    }

    public void Dispose() => Reader.Dispose();
}
