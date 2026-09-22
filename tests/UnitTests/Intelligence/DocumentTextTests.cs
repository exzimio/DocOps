using DocOps.Domain.Intelligence;

namespace DocOps.UnitTests.Intelligence;

/// <summary>
/// <see cref="DocumentText"/> is what makes "never present invented citations" a rule the code
/// enforces rather than a promise a provider makes.
/// </summary>
public class DocumentTextTests
{
    private const string Contract = """
        CONTRATO
        ========
        Entre as partes abaixo.

        CLÁUSULA 2.ª — VIGÊNCIA
        O contrato vigora até 30 de setembro de 2028.
        """;

    [Fact]
    public void Numbers_lines_and_tracks_the_section_they_belong_to()
    {
        DocumentText text = new(Contract);

        TextLine line = text.Lines.Single(l => l.Text.Contains("vigora", StringComparison.Ordinal));

        Assert.Equal("Cláusula 2.ª — vigência", line.Section);
        Assert.Equal(1, line.Page);
    }

    [Fact]
    public void Drops_underlines_and_does_not_mistake_data_for_a_heading()
    {
        DocumentText text = new("""
            FATURA
            ======
            IBAN PT50 0000 0000 0000
            Total: 10,00 €
            """);

        Assert.DoesNotContain(text.Lines, l => l.Text.Contains("====", StringComparison.Ordinal));
        Assert.Equal("Fatura", text.Lines.Single(l => l.Text.StartsWith("Total", StringComparison.Ordinal)).Section);
    }

    [Fact]
    public void A_form_feed_starts_a_new_page()
    {
        DocumentText text = new("primeira página\fsegunda página\ncontinua");

        Assert.Equal(1, text.Lines[0].Page);
        Assert.Equal(2, text.Lines.Single(l => l.Text == "segunda página").Page);
        Assert.Equal(2, text.Lines.Single(l => l.Text == "continua").Page);
    }

    [Fact]
    public void Verifies_a_quotation_that_is_on_the_line_it_names()
    {
        DocumentText text = new(Contract);
        TextLine line = text.Lines.Single(l => l.Text.Contains("vigora", StringComparison.Ordinal));

        Assert.True(text.Verifies(new TextReference(line.Number, 1, line.Section, "vigora até 30 de setembro")));

        // Accents and spacing do not matter; the words do.
        Assert.True(text.Verifies(new TextReference(line.Number, 1, null, "VIGORA  ATE 30 de Setembro")));
    }

    [Theory]
    [InlineData(6, "vigora até 31 de dezembro de 2030")]
    [InlineData(3, "vigora até 30 de setembro")]
    [InlineData(99, "vigora")]
    [InlineData(6, "")]
    public void Rejects_a_quotation_that_is_invented_or_misplaced(int lineNumber, string excerpt)
    {
        DocumentText text = new(Contract);

        Assert.False(text.Verifies(new TextReference(lineNumber, 1, null, excerpt)));
    }

    [Fact]
    public void Rejects_a_quotation_that_names_the_wrong_page()
    {
        DocumentText text = new(Contract);
        TextLine line = text.Lines.Single(l => l.Text.Contains("vigora", StringComparison.Ordinal));

        Assert.False(text.Verifies(new TextReference(line.Number, 2, null, "vigora")));
    }

    [Fact]
    public void Locates_the_line_a_quotation_comes_from()
    {
        DocumentText text = new(Contract);

        TextReference? found = text.Locate("30 de setembro de 2028");
        TextReference? invented = text.Locate("31 de dezembro de 2030");

        Assert.NotNull(found);
        Assert.True(text.Verifies(found));
        Assert.Null(invented);
    }
}
