using TraceGreen.Infrastructure.Postgres;

namespace TraceGreen.Tests.Infrastructure;

public class ConnectionStringsTests
{
    [Fact]
    public void ParsesTheComposeUrl()
    {
        var cs = ConnectionStrings.FromUrl("postgres://tracegreen:tracegreen@db:5432/tracegreen");

        Assert.Contains("Host=db", cs);
        Assert.Contains("Port=5432", cs);
        Assert.Contains("Database=tracegreen", cs);
        Assert.Contains("Username=tracegreen", cs);
        Assert.Contains("Password=tracegreen", cs);
    }

    [Fact]
    public void DecodesCredentialsDefaultsPortAndKeepsSslMode()
    {
        var cs = ConnectionStrings.FromUrl("postgresql://u%40x:p%3Ass@localhost/db?sslmode=require");

        Assert.Contains("Username=u@x", cs);
        Assert.Contains("Password=p:ss", cs);
        Assert.Contains("Port=5432", cs);
        Assert.Contains("SSL Mode=Require", cs);
    }

    [Fact]
    public void RejectsOtherSchemes() =>
        Assert.Throws<ArgumentException>(() => ConnectionStrings.FromUrl("mysql://a:b@c/d"));
}
