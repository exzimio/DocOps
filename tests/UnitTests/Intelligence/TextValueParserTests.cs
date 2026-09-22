using DocOps.Infrastructure.Intelligence;

namespace DocOps.UnitTests.Intelligence;

public class TextValueParserTests
{
    [Theory]
    [InlineData("1 906,50 €", "1906.50")]
    [InlineData("1.906,50", "1906.50")]
    [InlineData("1,906.50", "1906.50")]
    [InlineData("£3,250.00", "3250.00")]
    [InlineData("é de 84 000,00 € (oitenta e quatro mil euros)", "84000.00")]
    [InlineData("356,50", "356.50")]
    [InlineData("1906.5", "1906.50")]
    public void Reads_amounts_as_they_are_written(string text, string expected) =>
        Assert.Equal(expected, TextValueParser.ParseAmount(text));

    [Fact]
    public void Skips_a_percentage_to_reach_the_amount() =>
        Assert.Equal("356.50", TextValueParser.ParseAmount("(23%): 356,50 €"));

    [Fact]
    public void Does_not_read_a_registration_number_as_an_amount() =>
        // "VAT GB 123 4567 89" is a VAT number; reading it as "VAT = 123" would be a confident lie.
        Assert.Null(TextValueParser.ParseAmount("GB 123 4567 89"));

    [Fact]
    public void Accepts_a_whole_amount_only_when_a_currency_is_named()
    {
        Assert.Equal("4050.00", TextValueParser.ParseAmount("4050 EUR"));
        Assert.Null(TextValueParser.ParseAmount("4050"));
    }

    [Theory]
    [InlineData("03/09/2026", "2026-09-03")]
    [InlineData("3-9-2026", "2026-09-03")]
    [InlineData("2026-09-03", "2026-09-03")]
    [InlineData("produz efeitos a 1 de outubro de 2026 e vigora até 30 de setembro de 2028", "2026-10-01")]
    [InlineData("28 August 2026", "2026-08-28")]
    [InlineData("August 28, 2026", "2026-08-28")]
    public void Reads_dates_day_first_and_in_words(string text, string expected) =>
        Assert.Equal(expected, TextValueParser.ParseDate(text));

    [Theory]
    [InlineData("31/02/2026")]
    [InlineData("12/13/2026")]
    [InlineData("sem data")]
    public void Refuses_impossible_dates(string text) =>
        Assert.Null(TextValueParser.ParseDate(text));

    [Theory]
    [InlineData("Total: 10,00 €", "EUR")]
    [InlineData("Amount due: £3,250.00", "GBP")]
    [InlineData("USD 1,000.00", "USD")]
    [InlineData("Total: 10,00", null)]
    public void Recognises_currencies(string text, string? expected) =>
        Assert.Equal(expected, TextValueParser.ParseCurrency(text));

    [Theory]
    [InlineData("Nordhavn Logística, Lda., NIF 514 987 321, com sede na Rua", "Nordhavn Logística, Lda.")]
    [InlineData("lei portuguesa.", "lei portuguesa")]
    [InlineData("ENC-2026-00046      (anexo)", "ENC-2026-00046")]
    [InlineData("   ", null)]
    public void Cleans_a_text_value_read_after_a_label(string text, string? expected) =>
        Assert.Equal(expected, TextValueParser.CleanText(text));

    [Fact]
    public void Folding_keeps_positions_so_values_are_read_from_the_original_text()
    {
        const string original = "Data de emissão: 03/09/2026";
        string folded = TextValueParser.Fold(original);

        Assert.Equal(original.Length, folded.Length);
        Assert.Equal("data de emissao: 03/09/2026", folded);
    }
}
