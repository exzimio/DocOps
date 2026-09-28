namespace DocOps.UnitTests.Persistence;

/// <summary>
/// The guard against the one mistake that breaks the rule this whole product rests on.
///
/// <c>IgnoreQueryFilters()</c> is a <b>query-level</b> operator, not a subquery-level one. Written
/// inside the projection of another query — a count in a <c>Select</c>, say — it does not disable
/// the filters of that count: it disables them for the <b>entire</b> query it belongs to, including
/// the organisation filter of the outer one.
///
/// Nothing fails. No exception, no warning, no red test. The list simply starts including another
/// organisation's rows, and only someone who happens to sign in as the other organisation would ever
/// notice. It happened in phase 8, in the tag list: an administrator of Vértice could see the tags
/// of Asteria Group, and the probe against the real API is what caught it.
///
/// Using the operator at the top of a query is legitimate and common here — seeders run before any
/// organisation exists, the worker runs across all of them, and a soft-deleted row sometimes has to
/// be read back on purpose. Those uses are visible and deliberate. What this test forbids is the
/// invisible one: the operator buried inside a projection, where its true reach is not what it looks
/// like.
/// </summary>
public class QueryFilterUsageTests
{
    [Fact]
    public void No_query_disables_its_filters_from_inside_a_projection()
    {
        List<string> offences = [];

        foreach (string file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            offences.AddRange(FindInsideProjections(file, File.ReadAllText(file)));
        }

        Assert.True(
            offences.Count == 0,
            "`IgnoreQueryFilters()` dentro de uma projeção desliga os filtros da consulta inteira, "
            + "incluindo o da organização. Mova a subconsulta para uma consulta própria e escreva a "
            + "condição de organização à mão:\n  " + string.Join("\n  ", offences));
    }

    /// <summary>
    /// Walks the text tracking bracket depth, remembering which open parentheses were opened by a
    /// projection operator. An occurrence of the operator while any such parenthesis is still open
    /// is the mistake.
    ///
    /// Deliberately a scan and not a regular expression: what matters is whether the call sits
    /// inside the parentheses of a projection, and that is a question about nesting, which a regular
    /// expression cannot answer.
    /// </summary>
    private static IEnumerable<string> FindInsideProjections(string file, string source)
    {
        string[] projections = [".Select(", ".SelectMany(", ".GroupBy("];

        Stack<bool> openedByProjection = new();
        int line = 1;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];

            if (current == '\n')
            {
                line++;
                continue;
            }

            if (current == '(')
            {
                bool isProjection = projections.Any(name =>
                    i + 1 >= name.Length
                    && source.AsSpan(i + 1 - name.Length, name.Length).SequenceEqual(name));

                openedByProjection.Push(isProjection);
                continue;
            }

            if (current == ')')
            {
                if (openedByProjection.Count > 0)
                {
                    openedByProjection.Pop();
                }

                continue;
            }

            const string Call = "IgnoreQueryFilters";

            if (current == 'I'
                && i + Call.Length <= source.Length
                && source.AsSpan(i, Call.Length).SequenceEqual(Call)
                && openedByProjection.Contains(true))
            {
                yield return $"{Path.GetFileName(file)}:{line}";
            }
        }
    }

    private static string SourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DocOpsControlTower.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string path = Path.Combine(directory.FullName, "src");

        Assert.True(Directory.Exists(path), $"não encontrei {path}");

        return path;
    }
}
