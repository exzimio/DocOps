using DocOps.Domain.Search;

namespace DocOps.UnitTests.Search;

/// <summary>
/// Folding is the one place text is made comparable. If it were done differently in two places, a
/// question could recognise a word the search then failed to find.
/// </summary>
public class SearchTextTests
{
    [Theory]
    [InlineData("Análise", "analise")]
    [InlineData("FATURA", "fatura")]
    [InlineData("Atlas Tecnologia, Lda.", "atlas tecnologia lda")]
    [InlineData("  espaços   a   mais  ", "espacos a mais")]
    [InlineData("FT-2026/0815", "ft 2026 0815")]
    [InlineData(null, "")]
    public void Folds_to_lower_case_without_accents_and_with_single_spaces(string? raw, string expected)
    {
        Assert.Equal(expected, SearchText.Fold(raw));
    }

    [Theory]
    [InlineData("fatura", "faturas")]
    [InlineData("faturas", "fatura")]
    [InlineData("contrato", "contratos")]
    [InlineData("encomendas", "encomenda")]
    [InlineData("contracts", "contrato")]
    public void Treats_a_word_and_its_variants_as_the_same_word(string left, string right)
    {
        Assert.True(SearchText.SameWord(left, right));
    }

    [Theory]
    [InlineData("relatorio", "relativo")]
    [InlineData("do", "documento")]
    [InlineData("fatura", "contrato")]
    [InlineData("rh", "relatorio")]
    public void Keeps_words_that_only_look_alike_apart(string left, string right)
    {
        Assert.False(SearchText.SameWord(left, right));
    }

    [Fact]
    public void Requires_short_words_to_match_exactly()
    {
        Assert.True(SearchText.SameWord("rh", "rh"));
        Assert.False(SearchText.SameWord("rh", "rhs"));
    }
}
