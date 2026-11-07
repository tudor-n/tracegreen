using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>The PDF import (F9): extract into a draft, a person checks it, confirm imports it like any invoice.</summary>
public class PdfImportEndpointTests(ChatWebAppFactory factory) : IClassFixture<ChatWebAppFactory>
{
    private static readonly byte[] SamplePdf = File.ReadAllBytes(TestPaths.Invoice("P1-bistrita.pdf"));
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DraftInvoice Draft(string number = "ZT-2026-PDF1") => new(
        "Zincora Turnatorie SRL", "RO99999968", "RO", number, "2026-10-22", "EUR", "12000.00",
        [
            new DraftLine("Zinc die-cast sensor housings, type ZH-220", "1200", "H87", "9840.00", "790700", "HS"),
            new DraftLine("Zamak covers for sensor housings", "1200", "H87", "2160.00", "790700", "HS"),
        ]);

    private void ModelReads(DraftInvoice draft) =>
        factory.Ai.Extract = _ => Task.FromResult(new ExtractOutcome(
            draft, new Dictionary<string, bool> { ["invoiceNumber"] = true, ["totalNet"] = true }, "openai:model-a", 1, null));

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private Task<HttpResponseMessage> Upload(byte[] bytes, string name = "invoice.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        return _client.PostAsync("/api/ingest/pdf/extract", form, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<(string DraftId, JsonElement Invoice)> Extract(byte[] pdf)
    {
        var response = await Upload(pdf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        return (body.GetProperty("draftId").GetString()!, body.GetProperty("invoice"));
    }

    private Task<HttpResponseMessage> Confirm(string draftId, object invoice) =>
        _client.PostAsJsonAsync("/api/ingest/pdf/confirm", new { draftId, invoice }, Ct);

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    private async Task<AuditEntry> LastAudit() =>
        (await factory.Services.GetRequiredService<IRepository>().ListAuditAsync(new Ctx(Ids.DemoWorkspace, "test"), 0, 10_000, Ct))[^1];

    [Fact]
    public async Task ADraftComesBackWithWhatTheTextShowsAndNothingIsImported()
    {
        await Reset();
        ModelReads(Draft());
        var nodesBefore = (await factory.Services.GetRequiredService<IRepository>().ListNodesAsync(new Ctx(Ids.DemoWorkspace, "t"), Ct)).Count;

        var response = await Upload(SamplePdf);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("ZT-2026-PDF1", body.GetProperty("invoice").GetProperty("invoiceNumber").GetString());
        Assert.True(body.GetProperty("found").GetProperty("totalNet").GetBoolean());
        Assert.Equal("openai:model-a", body.GetProperty("provider").GetString());
        Assert.Equal(32, body.GetProperty("draftId").GetString()!.Length);
        Assert.Equal(nodesBefore, (await factory.Services.GetRequiredService<IRepository>().ListNodesAsync(new Ctx(Ids.DemoWorkspace, "t"), Ct)).Count);
    }

    [Fact]
    public async Task ConfirmingTheDraftImportsAnEstimatedNodeAndAuditsWhatWasChecked()
    {
        await Reset();
        ModelReads(Draft());
        var (draftId, invoice) = await Extract(SamplePdf);

        var response = await Confirm(draftId, invoice);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var node = (await Json(response)).GetProperty("node");
        Assert.Equal("ESTIMATED", node.GetProperty("status").GetString());
        Assert.Equal("Zincora Turnatorie SRL", node.GetProperty("label").GetString());
        Assert.Equal("zinc_diecast", node.GetProperty("classification").GetProperty("categoryId").GetString());
        Assert.Equal("invoice", node.GetProperty("classification").GetProperty("source").GetString());
        Assert.Equal(12000m, node.GetProperty("spendEur").GetDecimal());
        var audit = await LastAudit();
        Assert.Equal("invoice.imported", audit.Action);
        Assert.Equal("pdf", audit.Payload["documentKind"]!.GetValue<string>());
        Assert.Equal("none", audit.Payload["editedFields"]!.GetValue<string>());
        Assert.Equal("openai:model-a", audit.Payload["extractedBy"]!.GetValue<string>());
        Assert.Equal(64, audit.Payload["documentSha256"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task TheFieldsAPersonChangedAreAudited()
    {
        await Reset();
        ModelReads(Draft() with { TotalNet = "1200.00" });
        var (draftId, _) = await Extract(SamplePdf);

        var response = await Confirm(draftId, Draft() with { Lines = [Draft().Lines[0] with { Quantity = "1.200" }, Draft().Lines[1]] });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("totalNet,lines.0.quantity", (await LastAudit()).Payload["editedFields"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheSamePdfTwiceIsADuplicateAndSoIsTheSameInvoiceFromAnotherPdf()
    {
        await Reset();
        ModelReads(Draft());
        var (draftId, invoice) = await Extract(SamplePdf);
        var nodeId = (await Json(await Confirm(draftId, invoice))).GetProperty("node").GetProperty("id").GetGuid();

        var again = await Upload(SamplePdf);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var problem = await Json(again);
        Assert.Equal("Duplicate", problem.GetProperty("code").GetString());
        Assert.Equal(nodeId, problem.GetProperty("nodeId").GetGuid());

        var other = SamplePdf.Concat("\n% another copy\n"u8.ToArray()).ToArray();
        var (secondDraft, secondInvoice) = await Extract(other);
        await AssertProblem(await Confirm(secondDraft, secondInvoice), HttpStatusCode.Conflict, "Duplicate");
    }

    [Fact]
    public async Task FieldsThatDoNotHoldAre422AndKeepTheDraft()
    {
        await Reset();
        ModelReads(Draft());
        var (draftId, _) = await Extract(SamplePdf);

        await AssertProblem(await Confirm(draftId, Draft() with { Lines = [] }), HttpStatusCode.UnprocessableEntity, "InvalidInvoiceFields");
        await AssertProblem(await Confirm(draftId, Draft() with { IssueDate = "22.10.2026" }), HttpStatusCode.UnprocessableEntity, "InvalidInvoiceFields");
        await AssertProblem(
            await Confirm(draftId, Draft() with { Lines = [Draft().Lines[0] with { UnitCode = "BOX" }] }), HttpStatusCode.UnprocessableEntity, "InvalidInvoiceFields");
        await AssertProblem(await Confirm(draftId, Draft() with { Currency = "RON" }), HttpStatusCode.UnprocessableEntity, "InvalidInvoiceFields");

        Assert.Equal(HttpStatusCode.Created, (await Confirm(draftId, Draft())).StatusCode);
    }

    [Fact]
    public async Task AnUnknownDraftIs410()
    {
        await AssertProblem(await Confirm("0123456789abcdef0123456789abcdef", Draft()), HttpStatusCode.Gone, "DraftExpired");
    }

    [Fact]
    public async Task AFileThatIsNotAPdfIs415AndTheAiIsNeverAsked()
    {
        factory.Ai.PdfsSent.Clear();

        await AssertProblem(await Upload("<Invoice/>"u8.ToArray(), "invoice.xml"), HttpStatusCode.UnsupportedMediaType, "NotPdf");
        Assert.Empty(factory.Ai.PdfsSent);
    }

    [Theory]
    [InlineData(ExtractFailure.NoText, HttpStatusCode.UnprocessableEntity, "PdfNoText")]
    [InlineData(ExtractFailure.Unreadable, HttpStatusCode.UnprocessableEntity, "PdfUnreadable")]
    [InlineData(ExtractFailure.Unavailable, HttpStatusCode.ServiceUnavailable, "ExtractionUnavailable")]
    public async Task WhatTheAiServiceCouldNotReadIsSaid(ExtractFailure failure, HttpStatusCode status, string code)
    {
        await Reset();
        factory.Ai.Extract = _ => Task.FromResult(ExtractOutcome.Failed(failure));

        await AssertProblem(await Upload(SamplePdf), status, code);
    }

    [Fact]
    public async Task AFailingAiServiceIs503()
    {
        await Reset();
        factory.Ai.Extract = _ => throw new HttpRequestException("down");

        await AssertProblem(await Upload(SamplePdf), HttpStatusCode.ServiceUnavailable, "ExtractionUnavailable");
    }
}
