using System.Text.RegularExpressions;

namespace DocOps.UnitTests.Persistence;

/// <summary>
/// The guard against the mistake this test suite makes easy.
///
/// A test that asserts a fixed number of rows is true the day it is written and becomes false the
/// day another suite creates legitimate data, and the failure looks like a regression in the product
/// when it is a regression in the test.
///
/// A count over a list the server builds — a paged result, a table of documents, a list of
/// submissions — is in practice an assertion about execution order, because the integration tests
/// share one database and the end-to-end tests run against the development one. The rule that
/// replaces it: **count what the test itself created, check identity for everything else.** A test
/// of isolation asserts that no row of organisation A appears on B's side, never that B has none.
///
/// Counting is still right for things the test made — the two nodes it just drew, the second version
/// it just uploaded, the reply it just sent. Those exceptions are listed below by name, each with
/// its reason, so that adding one is a decision rather than an oversight.
/// </summary>
public class TestSuiteConventionTests
{
    /// <summary>
    /// Counts over server-built listings that are deliberately fixed, each with the reason.
    /// Keyed by "file:line-fragment" so that moving code does not silently re-permit something else.
    /// </summary>
    private static readonly (string File, string Fragment, string Reason)[] AllowedCounts =
    [
        ("SearchTests.cs", "zzzznaoexiste",
            "Pesquisa por um termo que não existe em nenhum documento e nunca existirá: zero é a "
            + "resposta correta e não depende de quem criou o quê."),
    ];

    /// <summary>
    /// Selectors that name a list the server filled. Counting rows of one of these is the mistake;
    /// counting `.react-flow__node` is not, because those are the nodes the test just drew.
    /// </summary>
    private static readonly string[] ServerListSelectors =
    [
        "tbody tr", "getByRole('listitem'", "getByRole('list'", "getByRole('region'", "getByRole('row'"
    ];

    [Fact]
    public void No_integration_test_asserts_a_fixed_total_from_a_server_listing()
    {
        // TotalCount only ever comes off a PagedResult, which only ever comes off a listing
        // endpoint. There is no way for a test to have created "the total".
        Regex pattern = new(@"Assert\.Equal\(\s*\d+\s*,[^;]*?TotalCount", RegexOptions.Compiled);

        List<string> offenders = [];

        foreach (string file in Directory.GetFiles(TestsRoot("IntegrationTests"), "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!pattern.IsMatch(lines[i]) || IsAllowed(file, lines, i))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}:{i + 1} — {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Estes testes afirmam um total fixo vindo de uma listagem do servidor, o que os torna "
            + "reféns da ordem de execução. Verifique identidade em vez do total — ou, se o número "
            + "for mesmo independente do que outros testes criam, acrescente-o a AllowedCounts com "
            + "a razão:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_end_to_end_test_asserts_a_fixed_number_of_rows_in_a_server_list()
    {
        Regex pattern = new(@"toHaveCount\(\s*[1-9]\d*\s*\)", RegexOptions.Compiled);

        List<string> offenders = [];

        foreach (string file in Directory.GetFiles(TestsRoot("e2e/specs"), "*.ts", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                // The assertion and the locator are often on different lines, so the surrounding
                // few lines are what decides whether this counts a server list.
                string window = string.Join(' ', lines.Skip(Math.Max(0, i - 3)).Take(5));

                if (!pattern.IsMatch(lines[i])
                    || !ServerListSelectors.Any(selector => window.Contains(selector, StringComparison.Ordinal))
                    || IsAllowed(file, lines, i))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}:{i + 1} — {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Estes testes contam linhas de uma lista construída pelo servidor, que outras suites "
            + "alimentam. Afirme o que tem de estar lá e o que não pode lá estar, em vez do total:"
            + "\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// An assertion is allowed when it is listed by file and fragment, or when the line above it
    /// carries an explicit waiver. The waiver has to say why, because "// contagem-própria" alone
    /// would become a way of silencing the test rather than answering it.
    /// </summary>
    private static bool IsAllowed(string file, string[] lines, int index)
    {
        string name = Path.GetFileName(file);

        // The fragment that justifies a count is usually on a nearby line — the query that can only
        // ever return nothing, for instance — rather than on the assertion itself.
        string context = string.Join(' ', lines.Skip(Math.Max(0, index - 6)).Take(8));

        if (AllowedCounts.Any(allowed =>
                string.Equals(allowed.File, name, StringComparison.Ordinal)
                && context.Contains(allowed.Fragment, StringComparison.Ordinal)))
        {
            return true;
        }

        const string waiver = "contagem-própria:";

        return index > 0
            && lines[index - 1].Contains(waiver, StringComparison.Ordinal)
            && lines[index - 1].TrimEnd().Length > lines[index - 1].IndexOf(waiver, StringComparison.Ordinal)
                + waiver.Length + 10;
    }

    /// <summary>Walks up from the test binaries until the solution file appears.</summary>
    private static string TestsRoot(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DocOpsControlTower.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string path = Path.Combine(directory.FullName, "tests", relative);

        Assert.True(Directory.Exists(path), $"não encontrei {path}");

        return path;
    }
}
