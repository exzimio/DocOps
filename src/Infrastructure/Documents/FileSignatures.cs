using DocOps.Domain.Common;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// Confere que o conteúdo de um ficheiro é aquilo que o nome dele promete.
///
/// A extensão e o tipo de conteúdo vêm ambos do cliente e ambos se forjam: um executável chamado
/// `relatorio.pdf`, declarado como `application/pdf`, passava nas duas verificações que existiam. O
/// que não se forja com a mesma facilidade são os primeiros bytes, porque são o que define o
/// formato de verdade.
///
/// Isto não é antivírus e não pretende ser. É a verificação mínima que impede que o repositório
/// guarde uma coisa a fingir ser outra, e que depois a sirva com um tipo que não lhe corresponde.
///
/// Os formatos de texto não têm assinatura nenhuma, e por isso a regra para eles é a inversa: não
/// podem começar por nenhuma assinatura conhecida de binário, e não podem trazer bytes nulos, que
/// texto não tem.
/// </summary>
public static class FileSignatures
{
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    /// <summary>Os formatos Office modernos são arquivos ZIP: começam por «PK».</summary>
    private static readonly byte[] Zip = [0x50, 0x4B];

    /// <summary>Os formatos Office antigos são documentos compostos OLE2.</summary>
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly Dictionary<string, byte[][]> Expected = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = [Pdf],
        [".png"] = [Png],
        [".jpg"] = [Jpeg],
        [".jpeg"] = [Jpeg],
        [".docx"] = [Zip],
        [".xlsx"] = [Zip],
        [".pptx"] = [Zip],
        [".doc"] = [Ole2],
        [".xls"] = [Ole2]
    };

    /// <summary>As assinaturas que um ficheiro de texto nunca pode ter.</summary>
    private static readonly byte[][] NeverText = [Pdf, Png, Jpeg, Zip, Ole2, [0x4D, 0x5A], [0x7F, 0x45, 0x4C, 0x46]];

    /// <summary>Quantos bytes bastam para decidir. A maior assinatura tem oito.</summary>
    public const int SampleSize = 512;

    /// <summary>
    /// Confirma que a amostra corresponde à extensão, ou lança.
    /// </summary>
    public static void EnsureMatches(ReadOnlySpan<byte> sample, string extension)
    {
        if (Expected.TryGetValue(extension, out byte[][]? assinaturas))
        {
            foreach (byte[] assinatura in assinaturas)
            {
                if (sample.StartsWith(assinatura))
                {
                    return;
                }
            }

            throw new DomainException(
                $"O conteúdo do ficheiro não corresponde a '{extension}'.", "content_does_not_match_extension");
        }

        // Texto: não há assinatura a confirmar, só assinaturas a excluir.
        foreach (byte[] binario in NeverText)
        {
            if (sample.StartsWith(binario))
            {
                throw new DomainException(
                    $"O conteúdo do ficheiro não é texto, apesar da extensão '{extension}'.",
                    "content_does_not_match_extension");
            }
        }

        if (sample.Contains((byte)0))
        {
            throw new DomainException(
                $"O conteúdo do ficheiro não é texto, apesar da extensão '{extension}'.",
                "content_does_not_match_extension");
        }
    }

    /// <summary>
    /// O tipo de conteúdo que corresponde a esta extensão.
    ///
    /// É este que fica guardado, e não o que o cliente declarou: depois de os bytes confirmarem o
    /// formato, o que o cliente disse deixa de ter utilidade nenhuma, e continuar a guardá-lo seria
    /// manter entrada não confiável a atravessar o sistema até ao cabeçalho de uma resposta.
    /// </summary>
    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".csv" => "text/csv",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".doc" => "application/msword",
        ".xls" => "application/vnd.ms-excel",
        _ => "text/plain"
    };
}
