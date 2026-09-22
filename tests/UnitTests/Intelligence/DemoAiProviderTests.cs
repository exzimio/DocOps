using DocOps.Application.Intelligence;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Intelligence;

namespace DocOps.UnitTests.Intelligence;

/// <summary>
/// The demonstration provider reads realistic documents — not the metadata mirrored back as text —
/// and every value it returns must point at a line that really says it.
/// </summary>
public class DemoAiProviderTests
{
    private readonly DemoAiProvider _provider = new();

    [Fact]
    public void Declares_itself_a_demonstration_and_not_a_model()
    {
        Assert.Equal(AiSource.Demo, _provider.Provider.Source);
        Assert.Contains("Não é um modelo de linguagem", _provider.Provider.Description);
    }

    [Theory]
    [InlineData("fatura-atlas-tecnologia.txt", "SupplierInvoice")]
    [InlineData("invoice-brightline-uk.txt", "SupplierInvoice")]
    [InlineData("contrato-nordhavn-logistica.txt", "Contract")]
    [InlineData("encomenda-verdemar.txt", "PurchaseOrder")]
    public async Task Recognises_the_type_of_each_sample(string file, string expectedType)
    {
        DocumentText text = SampleCatalogue.Read(file);

        ClassificationResult result = await _provider.ClassifyAsync(text, SampleCatalogue.All);

        ClassificationCandidate best = Assert.IsType<ClassificationCandidate>(result.Candidates[0]);
        Assert.Equal(expectedType, best.TypeCode);
        Assert.True(best.Confidence >= AiReview.MediumConfidence, $"confidence was {best.Confidence}");
        Assert.NotEmpty(best.Evidence);
        Assert.All(best.Evidence, evidence => Assert.True(text.Verifies(evidence)));
    }

    [Fact]
    public async Task An_unambiguous_invoice_is_classified_with_high_confidence()
    {
        ClassificationResult result = await _provider.ClassifyAsync(
            SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.All);

        Assert.Equal(ConfidenceBand.High, AiReview.Band(result.Candidates[0].Confidence));
    }

    [Fact]
    public async Task Reads_every_field_of_a_portuguese_invoice()
    {
        Dictionary<string, string?> values = await ExtractAsync("fatura-atlas-tecnologia.txt", SampleCatalogue.Invoice);

        Assert.Equal("Atlas Tecnologia, Lda.", values["supplier"]);
        Assert.Equal("FT 2026/0815", values["invoiceNumber"]);
        Assert.Equal("2026-09-03", values["invoiceDate"]);
        Assert.Equal("2026-10-03", values["dueDate"]);
        Assert.Equal("1550.00", values["netAmount"]);
        Assert.Equal("356.50", values["vatAmount"]);
        Assert.Equal("1906.50", values["totalAmount"]);
        Assert.Equal("EUR", values["currency"]);
        Assert.Equal("ENC-2026-00045", values["purchaseOrder"]);
    }

    [Fact]
    public async Task Reads_an_english_invoice_and_reports_what_it_does_not_contain()
    {
        Dictionary<string, string?> values = await ExtractAsync("invoice-brightline-uk.txt", SampleCatalogue.Invoice);

        Assert.Equal("Brightline Analytics Ltd", values["supplier"]);
        Assert.Equal("BL-2026-0418", values["invoiceNumber"]);
        Assert.Equal("2026-08-28", values["invoiceDate"]);
        Assert.Equal("3250.00", values["netAmount"]);
        Assert.Equal("0.00", values["vatAmount"]);
        Assert.Equal("3250.00", values["totalAmount"]);
        Assert.Equal("GBP", values["currency"]);

        // Not in the document, so not in the result. Guessing would be worse than saying nothing.
        Assert.Null(values["dueDate"]);
        Assert.Null(values["purchaseOrder"]);
    }

    [Fact]
    public async Task Reads_the_clauses_of_a_contract()
    {
        Dictionary<string, string?> values = await ExtractAsync("contrato-nordhavn-logistica.txt", SampleCatalogue.Contract);

        Assert.Equal("Nordhavn Logística, Lda.", values["counterparty"]);
        Assert.Equal("Prestação de serviços", values["contractType"]);
        Assert.Equal("2026-10-01", values["startDate"]);
        Assert.Equal("2028-09-30", values["endDate"]);
        Assert.Equal("84000.00", values["contractValue"]);
        Assert.Equal("90", values["noticeDays"]);
        Assert.Equal("lei portuguesa", values["governingLaw"]);
    }

    [Fact]
    public async Task Reads_a_purchase_order()
    {
        Dictionary<string, string?> values = await ExtractAsync("encomenda-verdemar.txt", SampleCatalogue.PurchaseOrder);

        Assert.Equal("Verdemar Serviços Ambientais, Lda.", values["supplier"]);
        Assert.Equal("ENC-2026-00058", values["orderNumber"]);
        Assert.Equal("2026-09-10", values["orderDate"]);
        Assert.Equal("4050.00", values["totalAmount"]);
        Assert.Equal("2026-09-24", values["deliveryDate"]);
    }

    [Theory]
    [InlineData("fatura-atlas-tecnologia.txt")]
    [InlineData("invoice-brightline-uk.txt")]
    [InlineData("contrato-nordhavn-logistica.txt")]
    [InlineData("encomenda-verdemar.txt")]
    public async Task Every_value_found_points_at_a_line_that_really_contains_it(string file)
    {
        DocumentText text = SampleCatalogue.Read(file);

        foreach (DocumentTypeProfile type in SampleCatalogue.All)
        {
            ExtractionResult result = await _provider.ExtractAsync(text, type);

            foreach (ExtractedField field in result.Fields.Where(f => f.Value is not null))
            {
                Assert.NotNull(field.Evidence);
                Assert.True(text.Verifies(field.Evidence), $"{type.Code}.{field.FieldCode} cites line {field.Evidence.Line}");
            }

            Assert.All(result.Fields.Where(f => f.Value is null), field => Assert.Null(field.Evidence));
        }
    }

    [Fact]
    public async Task The_same_document_always_produces_the_same_result()
    {
        DocumentText text = SampleCatalogue.Read("fatura-atlas-tecnologia.txt");

        ExtractionResult first = await _provider.ExtractAsync(text, SampleCatalogue.Invoice);
        ExtractionResult second = await _provider.ExtractAsync(text, SampleCatalogue.Invoice);

        Assert.Equal(first.Fields, second.Fields);
    }

    [Fact]
    public async Task Derives_a_missing_vat_amount_from_net_and_total_with_modest_confidence()
    {
        DocumentText text = new("""
            FATURA
            Fatura n.º: FT 12
            Total ilíquido: 100,00 €
            Total a pagar: 123,00 €
            """);

        ExtractionResult result = await _provider.ExtractAsync(text, SampleCatalogue.Invoice);
        ExtractedField vat = result.Fields.Single(f => f.FieldCode == "vatAmount");

        Assert.Equal("23.00", vat.Value);
        Assert.Equal(ConfidenceBand.Medium, AiReview.Band(vat.Confidence));
        Assert.True(text.Verifies(vat.Evidence!));
    }

    [Fact]
    public async Task A_document_without_text_produces_no_classification_and_no_values()
    {
        DocumentText empty = new(null);

        ClassificationResult classification = await _provider.ClassifyAsync(empty, SampleCatalogue.All);
        ExtractionResult extraction = await _provider.ExtractAsync(empty, SampleCatalogue.Invoice);

        Assert.Empty(classification.Candidates);
        Assert.All(extraction.Fields, field => Assert.Null(field.Value));
    }

    [Fact]
    public async Task Answers_a_question_from_the_field_that_answers_it_and_cites_the_line()
    {
        DocumentText text = SampleCatalogue.Read("fatura-atlas-tecnologia.txt");

        DocumentAnswer answer = await _provider.AnswerAsync(text, SampleCatalogue.Invoice, "Qual é o total da fatura?");

        Assert.True(answer.Answered);
        Assert.Contains("906,50", answer.Text);
        Assert.Contains("EUR", answer.Text);
        TextReference citation = Assert.Single(answer.Citations);
        Assert.True(text.Verifies(citation));
        Assert.Contains("Total a pagar", citation.Excerpt);
    }

    [Fact]
    public async Task Answers_in_english_when_asked_in_english()
    {
        DocumentText text = SampleCatalogue.Read("contrato-nordhavn-logistica.txt");

        DocumentAnswer answer = await _provider.AnswerAsync(text, SampleCatalogue.Contract, "When does this contract expire?");

        Assert.True(answer.Answered);
        Assert.Contains("30 September 2028", answer.Text);
        Assert.Equal("Cláusula 2.ª — vigência", answer.Citations[0].Section);
    }

    [Fact]
    public async Task Says_so_when_the_document_does_not_answer_the_question()
    {
        DocumentAnswer answer = await _provider.AnswerAsync(
            SampleCatalogue.Read("fatura-atlas-tecnologia.txt"), SampleCatalogue.Invoice, "Qual é a cor do logótipo?");

        Assert.False(answer.Answered);
        Assert.Empty(answer.Citations);
        Assert.Contains("Não encontrei", answer.Text);
    }

    private async Task<Dictionary<string, string?>> ExtractAsync(string file, DocumentTypeProfile type)
    {
        ExtractionResult result = await _provider.ExtractAsync(SampleCatalogue.Read(file), type);
        return result.Fields.ToDictionary(f => f.FieldCode, f => f.Value);
    }
}
