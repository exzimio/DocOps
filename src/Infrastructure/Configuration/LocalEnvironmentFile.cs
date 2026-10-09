using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace DocOps.Infrastructure.Configuration;

/// <summary>
/// Lê o <c>.env</c> da raiz do repositório para a configuração. Só se usa em desenvolvimento.
///
/// O mesmo ficheiro serve o Docker Compose, os guiões de <c>scripts/</c> e a aplicação, para que cada
/// palavra-passe local exista num sítio só. Um valor pode referir outro já definido com
/// <c>${NOME}</c>, que é o que deixa as cadeias de ligação usar as palavras-passe sem as repetir.
///
/// <para>
/// As variáveis de ambiente verdadeiras ganham sempre: o ficheiro só preenche o que ninguém definiu.
/// É isso que impede a integração contínua, que define as suas, de ler por engano um <c>.env</c>.
/// </para>
///
/// <para>
/// Os testes de integração arrancam a API em desenvolvimento e definem <see cref="IgnoreVariable"/>:
/// sem isso, uma chave que eles não definem, como o fornecedor de IA, viria do <c>.env</c> de quem
/// os corre, e um teste podia passar a chamar um modelo verdadeiro.
/// </para>
/// </summary>
public static partial class LocalEnvironmentFile
{
    /// <summary>Com o valor <c>1</c>, o <c>.env</c> não é lido.</summary>
    public const string IgnoreVariable = "DOCOPS_IGNORE_ENV_FILE";

    public static void AddLocalEnvironmentFile(this IConfigurationBuilder configuration, string contentRoot)
    {
        if (Environment.GetEnvironmentVariable(IgnoreVariable) == "1")
        {
            return;
        }

        string? path = Find(contentRoot);

        if (path is null)
        {
            return;
        }

        Dictionary<string, string> raw = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            int equals = trimmed.IndexOf('=');

            if (trimmed.Length == 0 || trimmed.StartsWith('#') || equals <= 0)
            {
                continue;
            }

            string key = trimmed[..equals].Trim();
            string value = Interpolate(trimmed[(equals + 1)..].Trim(), raw);
            raw[key] = value;

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
            }
        }

        configuration.AddInMemoryCollection(values);
    }

    private static string Interpolate(string value, Dictionary<string, string> earlier) =>
        Reference().Replace(value, match =>
        {
            string name = match.Groups[1].Value;

            return Environment.GetEnvironmentVariable(name)
                ?? (earlier.TryGetValue(name, out string? known) ? known : match.Value);
        });

    /// <summary>
    /// Sobe a partir da raiz de conteúdo até encontrar o <c>.env</c>, e para na raiz do repositório:
    /// um <c>.env</c> acima dela pertence a outro projeto.
    /// </summary>
    private static string? Find(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return null;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex Reference();
}
