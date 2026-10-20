using System.Diagnostics;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain.Ai;

public class LineClassificationTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    private static ExtractedLine Line(string id, string description, string? code = null, string? list = null) =>
        new(id, description, 1m, "C62", 10m, null, code, list);

    private sealed class FakeAi(Func<IReadOnlyList<ClassificationInput>, CancellationToken, Task<IReadOnlyList<Classification>>> classify) : IAiClient
    {
        public int Calls { get; private set; }
        public string Name => "fake";

        public Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct)
        {
            Calls++;
            return classify(lines, ct);
        }

        public Task<IReadOnlyList<Suggestion>> SuggestAsync(SupplierNode node, IReadOnlyList<Alternative> candidates, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Suggestion>>([]);

        public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, null));
    }

    private static FakeAi Answering(string categoryId) =>
        new((lines, _) => Task.FromResult<IReadOnlyList<Classification>>(
            lines.Select(l => new Classification(l.LineId, categoryId, 0.7, "model says so", ClassificationSource.Model)).ToList()));

    [Fact]
    public async Task InvoiceCodeWinsWithoutCallingTheAi()
    {
        var ai = Answering("pcb");

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "some housing", "44410000", "CPV")], ai, Seed, Timeout, CancellationToken.None);

        var c = Assert.Single(result);
        Assert.Equal("zinc_diecast", c.CategoryId);
        Assert.Equal(ClassificationSource.Invoice, c.Source);
        Assert.Equal(1.0, c.Confidence);
        Assert.Equal(0, ai.Calls);
    }

    [Fact]
    public async Task CacheHitWithoutCallingTheAi()
    {
        var ai = Answering("pcb");

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "  automotive CONNECTORS, 12-pin sealed ")], ai, Seed, Timeout, CancellationToken.None);

        Assert.Equal("connectors", result[0].CategoryId);
        Assert.Equal(ClassificationSource.Cache, result[0].Source);
        Assert.Equal(0, ai.Calls);
    }

    [Fact]
    public async Task UnknownLinesGoToTheAiAndKeepModelSource()
    {
        var ai = Answering("plastics");

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "Injection-moulded bracket")], ai, Seed, Timeout, CancellationToken.None);

        Assert.Equal("plastics", result[0].CategoryId);
        Assert.Equal(ClassificationSource.Model, result[0].Source);
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task AiAnswerOutsideTheAllowListBecomesUnclassified()
    {
        var ai = Answering("unobtainium");

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "Mystery part")], ai, Seed, Timeout, CancellationToken.None);

        Assert.Equal(Ids.Unclassified, result[0].CategoryId);
        Assert.Equal(0, result[0].Confidence);
    }

    [Fact]
    public async Task HangingAiResolvesWithinTheTimeoutAsStub()
    {
        var ai = new FakeAi(async (_, ct) => { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct); return []; });
        var watch = Stopwatch.StartNew();

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "Mystery part")], ai, Seed, Timeout, CancellationToken.None);

        Assert.True(watch.Elapsed < Timeout + TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        Assert.Equal(Ids.Unclassified, result[0].CategoryId);
        Assert.Equal(ClassificationSource.Stub, result[0].Source);
    }

    [Fact]
    public async Task ThrowingAiNeverSurfaces()
    {
        var ai = new FakeAi((_, _) => throw new HttpRequestException("boom"));

        var result = await LineClassification.ClassifyLinesAsync([Line("1", "Mystery part")], ai, Seed, Timeout, CancellationToken.None);

        Assert.Equal(ClassificationSource.Stub, result[0].Source);
    }

    [Fact]
    public async Task ResultsPreserveInputOrderAcrossSources()
    {
        var ai = Answering("plastics");
        var lines = new[] { Line("a", "Mystery part"), Line("b", "x", "31710000", "CPV"), Line("c", "Automotive connectors, 12-pin sealed") };

        var result = await LineClassification.ClassifyLinesAsync(lines, ai, Seed, Timeout, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], result.Select(r => r.LineId));
        Assert.Equal([ClassificationSource.Model, ClassificationSource.Invoice, ClassificationSource.Cache], result.Select(r => r.Source));
    }
}
