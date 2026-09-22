using System.Globalization;
using System.Text;

namespace DocOps.Domain.Search;

/// <summary>
/// The one way text is made comparable across the search: lower case, no accents, and every run of
/// anything else reduced to a single space. The typed terms, the vocabulary the question reader
/// knows and the catalogue names it matches against all pass through here, so a comparison cannot
/// fail because two places folded the same word differently.
/// </summary>
public static class SearchText
{
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool pending = false;

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pending && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pending = false;
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                pending = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether two folded words are the same word for search purposes. There is no stemmer here, and
    /// there does not need to be one: two words that agree on their first six letters are, in the
    /// vocabulary this application deals in, the same word — which is how "faturas" finds the type
    /// "Fatura de fornecedor", and how the English "contracts" still finds "Contrato".
    ///
    /// Six is the floor because five already lets "relatorio" and "relativo" through. Shorter words
    /// must match exactly, which is why "do" does not find "documento". The reading is always shown
    /// to the person, so a word matched too generously is visible rather than silent.
    /// </summary>
    public static bool SameWord(string left, string right)
    {
        const int prefixFloor = 6;
        const int shortFloor = 4;

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (left.Length < shortFloor || right.Length < shortFloor)
        {
            return false;
        }

        if (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal))
        {
            return true;
        }

        int shared = 0;

        while (shared < left.Length && shared < right.Length && left[shared] == right[shared])
        {
            shared++;
        }

        return shared >= prefixFloor;
    }
}
