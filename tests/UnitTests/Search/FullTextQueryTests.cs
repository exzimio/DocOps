using DocOps.Domain.Search;

namespace DocOps.UnitTests.Search;

/// <summary>
/// The condition handed to <c>CONTAINSTABLE</c> is rebuilt from the words a person typed; none of
/// their punctuation survives as syntax. These tests are what keeps that true.
/// </summary>
public class FullTextQueryTests
{
    [Fact]
    public void Reads_each_word_as_its_own_term()
    {
        IReadOnlyList<SearchTerm> terms = FullTextQuery.Parse("atlas tecnologia");

        Assert.Equal(["atlas", "tecnologia"], terms.Select(term => term.Text));
        Assert.All(terms, term => Assert.False(term.IsPhrase));
    }

    [Fact]
    public void Keeps_a_quoted_group_together_as_one_phrase()
    {
        IReadOnlyList<SearchTerm> terms = FullTextQuery.Parse("\"prestação de serviços\" atlas");

        Assert.Equal("prestacao de servicos", terms[0].Text);
        Assert.True(terms[0].IsPhrase);
        Assert.Equal("atlas", terms[1].Text);
        Assert.False(terms[1].IsPhrase);
    }

    [Fact]
    public void Folds_accents_so_the_typed_word_and_the_indexed_word_agree()
    {
        Assert.Equal(["analise"], FullTextQuery.Parse("análise").Select(term => term.Text));
    }

    [Fact]
    public void Breaks_on_punctuation_exactly_where_the_index_does()
    {
        Assert.Equal(
            ["2026", "0815"],
            FullTextQuery.Parse("2026/0815").Select(term => term.Text));
    }

    [Fact]
    public void Drops_single_characters_that_would_match_almost_everything()
    {
        Assert.Empty(FullTextQuery.Parse("a e o"));
    }

    [Theory]
    [InlineData("fatura")]
    [InlineData("atlas tecnologia")]
    [InlineData("\"prestação de serviços\"")]
    public void Builds_a_condition_whose_every_term_is_quoted(string typed)
    {
        string condition = FullTextQuery.Build(typed)!;

        // Every quote in the condition is one this code wrote: each term is opened and closed, and
        // nothing sits between terms except the operator.
        Assert.Equal(0, condition.Count(character => character == '"') % 2);
        Assert.DoesNotContain("\"\"", condition, StringComparison.Ordinal);

        foreach (string part in condition.Split(" AND "))
        {
            Assert.StartsWith("\"", part, StringComparison.Ordinal);
            Assert.EndsWith("\"", part, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Appends_a_prefix_marker_to_a_word_but_not_to_a_phrase()
    {
        Assert.Equal("\"fatura*\"", FullTextQuery.Build("fatura"));
        Assert.Equal("\"prestacao de servicos\"", FullTextQuery.Build("\"prestação de serviços\""));
    }

    [Fact]
    public void Joins_terms_with_AND_so_every_word_must_be_present()
    {
        Assert.Equal("\"atlas*\" AND \"tecnologia*\"", FullTextQuery.Build("atlas tecnologia"));
    }

    /// <summary>
    /// A search that carries no searchable word must not run at all: an empty condition is a syntax
    /// error in SQL Server, not a request to match everything.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("- , . !")]
    public void Returns_nothing_to_search_for_when_there_is_nothing_to_search_for(string? typed)
    {
        Assert.Null(FullTextQuery.Build(typed));
    }

    /// <summary>
    /// The operators of the full-text grammar are not a way in. Whatever the person typed comes back
    /// as quoted words; it cannot become <c>OR</c>, <c>NEAR</c> or a stray quote.
    /// </summary>
    [Theory]
    [InlineData("fatura\" OR 1=1")]
    [InlineData("fatura NEAR((a,b),1)")]
    [InlineData("FORMSOF(INFLECTIONAL, x)")]
    [InlineData("a\" AND \"b")]
    public void Never_lets_the_grammar_through(string hostile)
    {
        string condition = FullTextQuery.Build(hostile)!;

        foreach (string part in condition.Split(" AND "))
        {
            string inside = part.Trim('"').TrimEnd('*');

            // What is inside the quotes is letters, digits and single spaces — nothing the grammar
            // can act on.
            Assert.All(inside, character => Assert.True(char.IsLetterOrDigit(character) || character == ' '));
        }
    }

    [Fact]
    public void Refuses_to_turn_a_pasted_page_into_a_query()
    {
        string pasted = string.Join(' ', Enumerable.Range(0, 200).Select(index => $"palavra{index}"));

        Assert.True(FullTextQuery.Parse(pasted).Count <= 12);
    }
}
