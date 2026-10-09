using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Workflows;

namespace DocOps.IntegrationTests;

/// <summary>
/// Percorrer uma listagem página a página tem de devolver cada linha exatamente uma vez.
///
/// Uma página não é uma fatia de um resultado guardado: é uma consulta independente, com o seu
/// próprio <c>OFFSET</c>. Se a ordenação empatar e não houver último critério de desempate, o SQL
/// Server é livre de devolver os empatados por ordens diferentes em cada consulta — e então um
/// registo aparece em duas páginas e outro em nenhuma, sem erro nenhum e sem nada que o denuncie.
///
/// Este teste não afirma contagens fixas nem uma ordem concreta, que é o que a guarda em
/// <c>TestSuiteConventionTests</c> proíbe e bem. Afirma uma relação: a união das páginas tem de ter
/// tantos elementos distintos quantos o total que o servidor declarou.
///
/// Páginas pequenas de propósito, para haver muitos cortes e, portanto, muitas oportunidades de o
/// corte cair a meio de um grupo empatado.
///
/// <b>Com o catálogo semeado, este teste passa com ou sem o desempate.</b> Com quarenta e uma
/// linhas e sem paralelismo, o SQL Server devolve sempre a mesma ordem. O desempate existe para não
/// depender de um comportamento que o motor nunca prometeu, e este teste é a rede para o dia em que
/// o volume, um índice novo ou um plano paralelo mudem essa ordem.
/// </summary>
[Collection(ApiCollection.Name)]
public class PaginationIntegrityTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    // Ordenar por estado empata em cheio: dezenas de documentos partilham o mesmo estado.
    [InlineData("/api/documents?sortBy=2&sortDescending=false")]
    [InlineData("/api/documents?sortBy=2&sortDescending=true")]
    // E por título, que também não é único.
    [InlineData("/api/documents?sortBy=0&sortDescending=false")]
    [InlineData("/api/documents")]
    public async Task Paging_through_the_document_list_returns_every_document_exactly_once(string route)
    {
        HttpClient client = await factory.SignInAsync(Administrator);

        List<Guid> seen = [];
        long declaredTotal = 0;

        for (int page = 1; page <= 40; page++)
        {
            PagedResult<DocumentListItemDto>? result =
                await client.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
                    $"{route}{(route.Contains('?') ? '&' : '?')}page={page}&pageSize=3", Json);

            Assert.NotNull(result);
            declaredTotal = result.TotalCount;
            seen.AddRange(result.Items.Select(item => item.Id));

            if (result.Items.Count == 0)
            {
                break;
            }
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(declaredTotal, seen.Distinct().Count());
    }

    [Fact]
    public async Task Paging_through_the_task_list_returns_every_task_exactly_once()
    {
        HttpClient client = await factory.SignInAsync(Administrator);

        // As tarefas ordenam por «fechada ou não», depois pelo prazo, depois pela prioridade. Três
        // colunas de baixa cardinalidade: os empates são a regra, não a exceção.
        List<Guid> seen = [];
        long declaredTotal = 0;

        for (int page = 1; page <= 40; page++)
        {
            PagedResult<TaskListItemDto>? result =
                await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>(
                    $"/api/tasks?scope=All&page={page}&pageSize=3", Json);

            Assert.NotNull(result);
            declaredTotal = result.TotalCount;
            seen.AddRange(result.Items.Select(item => item.Id));

            if (result.Items.Count == 0)
            {
                break;
            }
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(declaredTotal, seen.Distinct().Count());
    }

    [Fact]
    public async Task Paging_through_the_workflow_instances_returns_every_instance_exactly_once()
    {
        HttpClient client = await factory.SignInAsync(Administrator);

        List<Guid> seen = [];
        long declaredTotal = 0;

        for (int page = 1; page <= 40; page++)
        {
            PagedResult<WorkflowInstanceListItemDto>? result =
                await client.GetFromJsonAsync<PagedResult<WorkflowInstanceListItemDto>>(
                    $"/api/workflows/instances?page={page}&pageSize=3", Json);

            Assert.NotNull(result);
            declaredTotal = result.TotalCount;
            seen.AddRange(result.Items.Select(item => item.Id));

            if (result.Items.Count == 0)
            {
                break;
            }
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(declaredTotal, seen.Distinct().Count());
    }
}
