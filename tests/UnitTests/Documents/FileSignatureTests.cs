using System.Text;
using DocOps.Domain.Common;
using DocOps.Infrastructure.Documents;

namespace DocOps.UnitTests.Documents;

/// <summary>
/// O ficheiro tem de ser aquilo que o nome dele promete.
///
/// A extensão e o tipo de conteúdo vêm ambos do cliente e forjam-se os dois: se a validação olhasse
/// só para eles, um executável chamado `relatorio.pdf` e declarado como `application/pdf` entraria
/// no repositório sem uma única objeção.
/// </summary>
public class FileSignatureTests
{
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] Executavel = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".PDF")]
    public void A_real_pdf_is_accepted(string extension) =>
        FileSignatures.EnsureMatches(Pdf, extension);

    [Fact]
    public void A_real_png_is_accepted() => FileSignatures.EnsureMatches(Png, ".png");

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    public void The_modern_office_formats_are_zip_archives(string extension) =>
        FileSignatures.EnsureMatches(Zip, extension);

    [Fact]
    public void An_executable_named_pdf_is_refused()
    {
        DomainException erro = Assert.Throws<DomainException>(
            () => FileSignatures.EnsureMatches(Executavel, ".pdf"));

        Assert.Equal("content_does_not_match_extension", erro.Code);
    }

    [Fact]
    public void An_executable_named_txt_is_refused()
    {
        // Texto não tem assinatura, por isso a regra é a inversa: não pode ter a de um binário.
        Assert.Throws<DomainException>(() => FileSignatures.EnsureMatches(Executavel, ".txt"));
    }

    [Fact]
    public void A_pdf_named_txt_is_refused() =>
        Assert.Throws<DomainException>(() => FileSignatures.EnsureMatches(Pdf, ".txt"));

    [Fact]
    public void Real_text_is_accepted()
    {
        byte[] texto = Encoding.UTF8.GetBytes("FATURA\nTotal a pagar: 1 906,50 EUR\n");

        FileSignatures.EnsureMatches(texto, ".txt");
        FileSignatures.EnsureMatches(texto, ".csv");
    }

    [Fact]
    public void Binary_rubbish_named_txt_is_refused()
    {
        // Sem assinatura conhecida, mas com bytes nulos: texto não os tem.
        byte[] lixo = [0x01, 0x02, 0x00, 0x03, 0x04];

        Assert.Throws<DomainException>(() => FileSignatures.EnsureMatches(lixo, ".txt"));
    }

    [Fact]
    public void An_empty_sample_of_text_is_accepted() =>
        FileSignatures.EnsureMatches(ReadOnlySpan<byte>.Empty, ".txt");

    [Fact]
    public void The_stored_content_type_comes_from_the_extension()
    {
        // O que o cliente declarou não entra aqui: depois de os bytes confirmarem o formato, o tipo
        // é uma consequência da extensão verificada.
        Assert.Equal("application/pdf", FileSignatures.ContentTypeFor(".pdf"));
        Assert.Equal("image/jpeg", FileSignatures.ContentTypeFor(".jpeg"));
        Assert.Equal("text/csv", FileSignatures.ContentTypeFor(".csv"));
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSignatures.ContentTypeFor(".XLSX"));
    }
}
