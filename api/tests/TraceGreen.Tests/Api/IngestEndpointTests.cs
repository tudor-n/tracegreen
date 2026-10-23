using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>Each test starts from the seed state through the reset route, since the class shares one host.</summary>
public class IngestEndpointTests : IClassFixture<TestWebAppFactory>
{
    private static readonly string N1Xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));

    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public IngestEndpointTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private Task<HttpResponseMessage> PostFile(string xml, string field = "file")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        form.Add(file, field, "invoice.xml");
        return _client.PostAsync("/api/ingest/ubl", form, Ct);
    }

    private Task<HttpResponseMessage> PostXml(string xml, string mediaType = "text/xml") =>
        _client.PostAsync("/api/ingest/ubl", new StringContent(xml, Encoding.UTF8, mediaType), Ct);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await Json(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        return body;
    }

    private async Task<decimal> TotalKg() =>
        (await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct)).GetProperty("kpis").GetProperty("totalKg").GetDecimal();

    [Fact]
    public async Task ImportingN1Returns201WithAnEstimatedNodeAndRaisesTheTotal()
    {
        await Reset();
        var before = await TotalKg();

        var response = await PostFile(N1Xml);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var node = body.GetProperty("node");
        Assert.Equal("ESTIMATED", node.GetProperty("status").GetString());
        Assert.Equal("Shenzhen Diecast Co.", node.GetProperty("label").GetString());
        Assert.Equal("spend", node.GetProperty("basis").GetString());
        Assert.Equal(21600m, node.GetProperty("carbonKg").GetDecimal());
        Assert.Equal("CN-RO", node.GetProperty("lane").GetProperty("id").GetString());
        Assert.Equal("air", node.GetProperty("lane").GetProperty("mode").GetString());
        Assert.Equal("invoice", node.GetProperty("classification").GetProperty("source").GetString());
        var kpis = body.GetProperty("kpis");
        Assert.Equal(5, kpis.GetProperty("nodeCount").GetInt32());
        Assert.Equal(before + 21600m, kpis.GetProperty("totalKg").GetDecimal());
        Assert.Equal(before + 21600m, await TotalKg());
    }

    [Fact]
    public async Task ImportPublishesNodeCreatedWithKpisThenAuditAppended()
    {
        await Reset();
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var before = bus.LastSeq(Ids.DemoWorkspace);

        var response = await PostFile(N1Xml);
        var nodeId = (await Json(response)).GetProperty("node").GetProperty("id").GetGuid();

        var events = bus.ReplaySince(Ids.DemoWorkspace, before).Events;
        Assert.Collection(events,
            e =>
            {
                var created = Assert.IsType<NodeCreated>(e);
                Assert.Equal(nodeId, created.Node.Id);
                Assert.Equal(5, created.Kpis.NodeCount);
                Assert.Equal(NodeStatus.Estimated, created.Node.Status);
            },
            e => Assert.Equal("invoice.imported", Assert.IsType<AuditAppended>(e).Entry.Action));
    }

    [Fact]
    public async Task XmlBodyIsAcceptedLikeAFile()
    {
        await Reset();

        var response = await PostXml(N1Xml, "application/xml");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DemoImportUsesN1FixedIdsAndASecondImportIsADuplicate()
    {
        await Reset();

        var first = await _client.PostAsync("/api/ingest/demo", null, Ct);
        var second = await _client.PostAsync("/api/ingest/demo", null, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var node = (await Json(first)).GetProperty("node");
        Assert.Equal(TestNodes.N1Id, node.GetProperty("id").GetGuid());
        Assert.Equal(TestNodes.N1InvoiceId, node.GetProperty("invoice").GetProperty("id").GetGuid());
        await AssertProblem(second, HttpStatusCode.Conflict, "Duplicate");
    }

    [Fact]
    public async Task UploadingTheDemoInvoiceAgainReturns409WithTheExistingNodeId()
    {
        await Reset();
        await _client.PostAsync("/api/ingest/demo", null, Ct);

        var response = await PostFile(N1Xml);

        var problem = await AssertProblem(response, HttpStatusCode.Conflict, "Duplicate");
        Assert.Equal(TestNodes.N1Id, problem.GetProperty("nodeId").GetGuid());
    }

    [Fact]
    public async Task DoctypeIs400()
    {
        var response = await PostXml("<!DOCTYPE x [<!ENTITY e \"x\">]><Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>");

        await AssertProblem(response, HttpStatusCode.BadRequest, "DoctypeForbidden");
    }

    [Fact]
    public async Task ADocumentThatIsNotAnInvoiceIs400()
    {
        var response = await PostXml("<Order xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Order-2\"/>");

        await AssertProblem(response, HttpStatusCode.BadRequest, "NotUblInvoice");
    }

    [Fact]
    public async Task AnInvoiceWithoutLinesIs422()
    {
        var start = N1Xml.IndexOf("<cac:InvoiceLine>", StringComparison.Ordinal);
        var end = N1Xml.IndexOf("</cac:InvoiceLine>", StringComparison.Ordinal) + "</cac:InvoiceLine>".Length;

        var response = await PostXml(N1Xml[..start] + N1Xml[end..]);

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "Schema");
    }

    [Fact]
    public async Task AnInvoiceOverTheParserCapIs413()
    {
        var response = await PostFile(N1Xml.Replace("</Invoice>", "<!--" + new string('x', 2_100_000) + "--></Invoice>"));

        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "TooLarge");
    }

    [Fact]
    public async Task ABodyOverTheRequestCapIs413()
    {
        var response = await PostXml(new string(' ', 4 * 1024 * 1024 + 1));

        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "TooLarge");
    }

    [Fact]
    public async Task AFormWithoutTheFileFieldIs400()
    {
        var response = await PostFile(N1Xml, field: "invoice");

        await AssertProblem(response, HttpStatusCode.BadRequest, "MissingFile");
    }

    [Fact]
    public async Task JsonIs415()
    {
        var response = await PostXml("{}", "application/json");

        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "UnsupportedMediaType");
    }

    [Fact]
    public async Task ANonEuroInvoiceIs422()
    {
        var response = await PostXml(N1Xml
            .Replace("<cbc:DocumentCurrencyCode>EUR", "<cbc:DocumentCurrencyCode>RON")
            .Replace("SDC-2026-1028", "SDC-2026-RON"));

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "UnsupportedCurrency");
    }

    [Fact]
    public async Task ASupplierCountryWithoutALaneIs422()
    {
        const string cn = "<cbc:IdentificationCode>CN</cbc:IdentificationCode>";
        var at = N1Xml.IndexOf(cn, StringComparison.Ordinal);

        var response = await PostXml(N1Xml[..at] + "<cbc:IdentificationCode>US</cbc:IdentificationCode>" + N1Xml[(at + cn.Length)..]);

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "NoLane");
    }

    [Fact]
    public async Task DeeplyNestedXmlIs422WithoutHoldingTheServer()
    {
        var deep = string.Concat(Enumerable.Repeat("<a>", 280_000)) + string.Concat(Enumerable.Repeat("</a>", 280_000));
        var started = DateTime.UtcNow;

        var response = await PostXml(N1Xml.Replace("<cbc:ID>SDC-2026-1028</cbc:ID>", "<cbc:ID>" + deep + "</cbc:ID>"));

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "Schema");
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("-48000.00", "positive")]
    [InlineData("0.00", "positive")]
    [InlineData("50000000000000000000000000000", "exceeds")]
    public async Task ATotalOutsideTheSaneRangeIs422(string total, string reason)
    {
        var xml = N1Xml.Replace("<cbc:TaxExclusiveAmount currencyID=\"EUR\">48000.00</cbc:TaxExclusiveAmount>",
            $"<cbc:TaxExclusiveAmount currencyID=\"EUR\">{total}</cbc:TaxExclusiveAmount>");

        var problem = await AssertProblem(await PostXml(xml), HttpStatusCode.UnprocessableEntity, "Schema");

        Assert.Contains(reason, problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("<cbc:InvoicedQuantity unitCode=\"TNE\">70000000000000000000000000000</cbc:InvoicedQuantity>", "exceeds")]
    [InlineData("<cbc:InvoicedQuantity unitCode=\"KGM\">-2300</cbc:InvoicedQuantity>", "negative")]
    public async Task AQuantityOutsideTheSaneRangeIs422(string quantity, string reason)
    {
        var xml = N1Xml.Replace("<cbc:InvoicedQuantity unitCode=\"KGM\">2300</cbc:InvoicedQuantity>", quantity);

        var problem = await AssertProblem(await PostXml(xml), HttpStatusCode.UnprocessableEntity, "Schema");

        Assert.Contains(reason, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ATruncatedMultipartUploadIs400()
    {
        var body = new StringContent(
            "--cut\r\nContent-Disposition: form-data; name=\"file\"; filename=\"invoice.xml\"\r\nContent-Type: text/xml\r\n\r\n<Invoice",
            Encoding.UTF8);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=cut");

        var response = await _client.PostAsync("/api/ingest/ubl", body, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest, "MissingFile");
    }

    [Fact]
    public async Task TwoDemoImportsAtTheSameInstantGiveOneNodeAndOneConflict()
    {
        await Reset();

        var responses = await Task.WhenAll(
            _client.PostAsync("/api/ingest/demo", null, Ct),
            _client.PostAsync("/api/ingest/demo", null, Ct));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var conflict = await AssertProblem(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "Duplicate");
        Assert.Equal(TestNodes.N1Id, conflict.GetProperty("nodeId").GetGuid());
    }

    [Fact]
    public async Task ThePromptInjectionInvoiceIsClassifiedFromTheCacheAsData()
    {
        await Reset();

        var response = await PostFile(File.ReadAllText(TestPaths.Invoice("adversarial.xml")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var classification = (await Json(response)).GetProperty("node").GetProperty("classification");
        Assert.Equal("wire_harness", classification.GetProperty("categoryId").GetString());
        Assert.Equal("cache", classification.GetProperty("source").GetString());
        Assert.True(classification.GetProperty("aiMapped").GetBoolean());
    }

    [Fact]
    public async Task ALineNobodyCanClassifyImportsAsUnclassifiedWithoutWaitingForTheAi()
    {
        await Reset();
        var xml = N1Xml
            .Replace("<cac:CommodityClassification><cbc:ItemClassificationCode listID=\"STI\">44410000</cbc:ItemClassificationCode></cac:CommodityClassification>", "")
            .Replace("Zinc die-cast sensor housings, type ZH-220", "Assorted spare parts lot 7");
        var started = DateTime.UtcNow;

        var response = await PostXml(xml);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        var classification = (await Json(response)).GetProperty("node").GetProperty("classification");
        Assert.Equal(Ids.Unclassified, classification.GetProperty("categoryId").GetString());
        Assert.Equal("stub", classification.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, classification.GetProperty("confidence").ValueKind);
        Assert.False(classification.GetProperty("aiMapped").GetBoolean());
    }

    [Fact]
    public async Task TheAuditChainStaysValidAfterAnImport()
    {
        await Reset();
        await PostFile(N1Xml);

        var repo = _factory.Services.GetRequiredService<IRepository>();
        var entries = await repo.ListAuditAsync(new Ctx(Ids.DemoWorkspace, "test"), 0, 1000, Ct);

        Assert.Equal("invoice.imported", entries[^1].Action);
        Assert.True(AuditChain.Verify(entries).Valid);
    }

    [Fact]
    public async Task TheSnapshotShowsTheImportedNodeExactlyAsTheEventDid()
    {
        await Reset();
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var before = bus.LastSeq(Ids.DemoWorkspace);
        await _client.PostAsync("/api/ingest/demo", null, Ct);
        var created = bus.ReplaySince(Ids.DemoWorkspace, before).Events.OfType<NodeCreated>().Single();

        var snapshot = await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct);

        var fromSnapshot = snapshot.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == TestNodes.N1Id);
        using var fromEvent = JsonDocument.Parse(TraceGreen.Api.Endpoints.EventsEndpoints.Serialize(created));
        Assert.Equal(fromEvent.RootElement.GetProperty("node").GetRawText(), fromSnapshot.GetRawText());
    }
}
