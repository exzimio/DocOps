using DocOps.Domain.Search;

namespace DocOps.UnitTests.Search;

/// <summary>
/// The excerpt shown under a result is cut from the document's own text, and the position it says to
/// mark must really be the match. An excerpt that highlights the wrong words is worse than none.
/// </summary>
public class SnippetExtractorTests
{
    private const string Invoice = """
        FATURA DE FORNECEDOR
        ====================
        Atlas Tecnologia, Lda.
        Rua das Flores, 120 — 4050-262 Porto

        Descrição: Licenças de software para a equipa de operações.
        Total a pagar: 1 906,50 €
        """;

    private static SearchSnippet Extract(string text, string typed) =>
        SnippetExtractor.Extract(text, FullTextQuery.Parse(typed))
        ?? throw new InvalidOperationException("Esperava um excerto.");

    [Fact]
    public void Marks_the_words_that_were_actually_found()
    {
        SearchSnippet snippet = Extract(Invoice, "licencas");

        Assert.Equal("Licenças", snippet.Text.Substring(snippet.Start, snippet.Length));
    }

    [Fact]
    public void Matches_without_accents_but_quotes_the_document_with_them()
    {
        SearchSnippet snippet = Extract(Invoice, "descricao");

        Assert.Equal("Descrição", snippet.Text.Substring(snippet.Start, snippet.Length));
    }

    [Fact]
    public void Matches_without_regard_to_case()
    {
        SearchSnippet snippet = Extract(Invoice, "atlas");

        Assert.Equal("Atlas", snippet.Text.Substring(snippet.Start, snippet.Length));
    }

    [Fact]
    public void Does_not_open_the_excerpt_inside_a_rule_of_separator_characters()
    {
        // The match sits far enough in that the window opens inside the rule; the excerpt starts at
        // the first real word instead. A separator later in the excerpt stays: it is what the
        // document says, and only the opening was ever unreadable.
        SearchSnippet snippet = Extract(new string('-', 200) + " contrato de arrendamento", "contrato");

        Assert.StartsWith("… contrato", snippet.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapses_the_line_breaks_so_the_excerpt_reads_as_one_line()
    {
        SearchSnippet snippet = Extract(Invoice, "porto");

        Assert.DoesNotContain('\n', snippet.Text);
        Assert.DoesNotContain("  ", snippet.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_where_it_cut_the_text()
    {
        string filler = string.Join(' ', Enumerable.Repeat("palavra", 60));
        SearchSnippet snippet = Extract($"{filler} alvo {filler}", "alvo");

        Assert.StartsWith("…", snippet.Text, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet.Text, StringComparison.Ordinal);
        Assert.Equal("alvo", snippet.Text.Substring(snippet.Start, snippet.Length));
    }

    [Fact]
    public void Returns_nothing_when_the_document_body_does_not_hold_the_terms()
    {
        // Legitimate: the document matched on its title or on a metadata value, not on its text.
        Assert.Null(SnippetExtractor.Extract(Invoice, FullTextQuery.Parse("northstar")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Returns_nothing_for_a_document_stored_without_text(string? text)
    {
        Assert.Null(SnippetExtractor.Extract(text, FullTextQuery.Parse("atlas")));
    }

    [Fact]
    public void Takes_the_first_term_it_finds_rather_than_insisting_on_the_first_typed()
    {
        // "northstar" is not in the text; the excerpt is built around the term that is.
        SearchSnippet snippet = Extract(Invoice, "northstar atlas");

        Assert.Equal("Atlas", snippet.Text.Substring(snippet.Start, snippet.Length));
    }
}
