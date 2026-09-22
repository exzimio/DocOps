using System.Net;
using System.Text;
using System.Text.Json;
using DocOps.Application.Intelligence;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Intelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocOps.UnitTests.Intelligence;

/// <summary>
/// The language-model provider, against a fake endpoint. What matters is not that it calls the API —
/// it is that nothing the model says reaches a reviewer unless the document backs it up.
/// </summary>
public class OpenAiCompatibleProviderTests
{
    private sealed class FakeEndpoint(Func<string, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            (HttpStatusCode status, string body) = reply(LastBody ?? string.Empty);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static string ModelSays(object content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = JsonSerializer.Serialize(content) } } } });

    private static (OpenAiCompatibleProvider Provider, FakeEndpoint Endpoint) Create(
        object modelReply, string endpoint = "https://api.openai.com/v1", HttpStatusCode status = HttpStatusCode.OK)
    {
        FakeEndpoint fake = new(_ => (status, ModelSays(modelReply)));
        AiOptions options = new()
        {
            Provider = AiOptions.OpenAiCompatibleProvider,
            Endpoint = endpoint,
            ApiKey = "test-key-never-logged",
            Model = "gpt-test"
        };

        return (new OpenAiCompatibleProvider(new HttpClient(fake), Options.Create(options), NullLogger<OpenAiCompatibleProvider>.Instance), fake);
    }

    [Fact]
    public void Declares_itself_a_model_and_not_the_demonstration()
    {
        (OpenAiCompatibleProvider provider, _) = Create(new { });

        Assert.Equal(AiSource.Model, provider.Provider.Source);
        Assert.Contains("gpt-test", provider.Provider.Name);
    }

    [Fact]
    public async Task Asks_for_json_deterministically_with_the_key_in_a_header()
    {
        (OpenAiCompatibleProvider provider, FakeEndpoint endpoint) = Create(new { fields = Array.Empty<object>() });

        await provider.ExtractAsync(SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice);

        Assert.Equal("https://api.openai.com/v1/chat/completions", endpoint.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", endpoint.LastRequest.Headers.Authorization!.Scheme);

        using JsonDocument body = JsonDocument.Parse(endpoint.LastBody!);
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("temperature").GetDecimal());

        // The key goes in the header, never in the payload that carries the document.
        Assert.DoesNotContain("test-key-never-logged", endpoint.LastBody);
    }

    [Fact]
    public async Task Speaks_the_azure_dialect_when_the_endpoint_is_azure()
    {
        (OpenAiCompatibleProvider provider, FakeEndpoint endpoint) = Create(
            new { fields = Array.Empty<object>() },
            endpoint: "https://asteria.openai.azure.com/openai/deployments/extraction");

        await provider.ExtractAsync(SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice);

        Assert.StartsWith(
            "https://asteria.openai.azure.com/openai/deployments/extraction/chat/completions?api-version=",
            endpoint.LastRequest!.RequestUri!.ToString());
        Assert.True(endpoint.LastRequest.Headers.Contains("api-key"));
        Assert.Null(endpoint.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task Keeps_a_value_only_when_the_words_it_was_read_from_are_in_the_document()
    {
        DocumentText text = SampleCatalogue.Read("fatura-atlas-tecnologia.txt");

        (OpenAiCompatibleProvider provider, _) = Create(new
        {
            fields = new object[]
            {
                // Backed by the document: kept, and pointed at the right line.
                new { code = "totalAmount", value = "1906.50", confidence = 0.97, quote = "Total a pagar: 1 906,50 €" },

                // Invented: a plausible value with a quote that is not in the document. Discarded.
                new { code = "purchaseOrder", value = "ENC-2026-99999", confidence = 0.95, quote = "Encomenda: ENC-2026-99999" },

                // A value in the wrong shape for its field is not suggested either.
                new { code = "invoiceDate", value = "early September", confidence = 0.9, quote = "Data de emissão: 03/09/2026" }
            }
        });

        ExtractionResult result = await provider.ExtractAsync(text, SampleCatalogue.Invoice);

        ExtractedField total = result.Fields.Single(f => f.FieldCode == "totalAmount");
        Assert.Equal("1906.50", total.Value);
        Assert.NotNull(total.Evidence);
        Assert.True(text.Verifies(total.Evidence));

        ExtractedField invented = result.Fields.Single(f => f.FieldCode == "purchaseOrder");
        Assert.Null(invented.Value);
        Assert.Null(invented.Evidence);

        Assert.Null(result.Fields.Single(f => f.FieldCode == "invoiceDate").Value);
    }

    [Fact]
    public async Task A_true_quote_does_not_vouch_for_a_different_value()
    {
        (OpenAiCompatibleProvider provider, _) = Create(new
        {
            fields = new object[]
            {
                new { code = "totalAmount", value = "9999.00", confidence = 0.99, quote = "Total a pagar: 1 906,50 €" },
                new { code = "supplier", value = "Northstar Supplies", confidence = 0.99, quote = "Atlas Tecnologia, Lda." }
            }
        });

        ExtractionResult result = await provider.ExtractAsync(
            SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice);

        Assert.Null(result.Fields.Single(f => f.FieldCode == "totalAmount").Value);
        Assert.Null(result.Fields.Single(f => f.FieldCode == "supplier").Value);
    }

    [Fact]
    public async Task Offers_only_the_types_it_was_given()
    {
        (OpenAiCompatibleProvider provider, _) = Create(new
        {
            candidates = new object[]
            {
                new { typeCode = "SupplierInvoice", confidence = 0.9, quote = "FATURA" },
                new { typeCode = "SomethingItMadeUp", confidence = 0.99, quote = "FATURA" }
            }
        });

        ClassificationResult result = await provider.ClassifyAsync(
            SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.All);

        ClassificationCandidate only = Assert.Single(result.Candidates);
        Assert.Equal("SupplierInvoice", only.TypeCode);
    }

    [Fact]
    public async Task An_answer_keeps_only_the_quotes_that_are_really_in_the_document()
    {
        DocumentText text = SampleCatalogue.Read("contrato-nordhavn-logistica.txt");

        (OpenAiCompatibleProvider provider, _) = Create(new
        {
            answered = true,
            answer = "O contrato termina a 30 de setembro de 2028.",
            quotes = new[] { "vigora até 30 de setembro de 2028", "cláusula 9.ª permite rescisão imediata" }
        });

        DocumentAnswer answer = await provider.AnswerAsync(text, SampleCatalogue.Contract, "Quando termina o contrato?");

        TextReference citation = Assert.Single(answer.Citations);
        Assert.True(text.Verifies(citation));
    }

    [Fact]
    public async Task A_failed_request_surfaces_as_an_error_for_the_run_to_record()
    {
        (OpenAiCompatibleProvider provider, _) = Create(new { }, status: HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.ExtractAsync(SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice));
    }

    [Fact]
    public async Task A_reply_that_is_not_the_requested_json_suggests_nothing()
    {
        FakeEndpoint fake = new(_ => (HttpStatusCode.OK,
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "Claro! Aqui está a fatura…" } } } })));

        OpenAiCompatibleProvider provider = new(
            new HttpClient(fake),
            Options.Create(new AiOptions { Provider = "OpenAiCompatible", Endpoint = "https://api.openai.com/v1", ApiKey = "k", Model = "m" }),
            NullLogger<OpenAiCompatibleProvider>.Instance);

        ExtractionResult result = await provider.ExtractAsync(SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice);

        Assert.All(result.Fields, field => Assert.Null(field.Value));
    }
}
