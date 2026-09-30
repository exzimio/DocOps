using DocOps.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>
/// O bloqueio que impede dois worker de correrem o mesmo trabalho.
///
/// Só se consegue provar contra SQL Server verdadeiro: <c>sp_getapplock</c> é do motor, não do EF,
/// e nenhum provedor em memória o tem. É mais um caso em que testar contra uma base de dados falsa
/// não provaria coisa nenhuma.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApplicationLockTests(DocOpsApiFactory factory)
{
    [Fact]
    public async Task Only_one_holder_at_a_time()
    {
        // Âmbitos diferentes, como dois processos: cada um pede o bloqueio por sua conta.
        using IServiceScope primeiro = factory.Services.CreateScope();
        using IServiceScope segundo = factory.Services.CreateScope();

        string nome = $"ensaio-{Guid.NewGuid():N}";

        await using IAsyncDisposable? tomado =
            await primeiro.ServiceProvider.GetRequiredService<IJobLock>().TryAcquireAsync(nome);

        Assert.NotNull(tomado);

        IAsyncDisposable? recusado =
            await segundo.ServiceProvider.GetRequiredService<IJobLock>().TryAcquireAsync(nome);

        // Recusa imediata, e não espera: um worker que esperasse acabaria a correr o trabalho logo
        // a seguir ao outro, que é precisamente o que isto existe para evitar.
        Assert.Null(recusado);
    }

    [Fact]
    public async Task The_lock_is_released_and_can_be_taken_again()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IJobLock locking = scope.ServiceProvider.GetRequiredService<IJobLock>();

        string nome = $"ensaio-{Guid.NewGuid():N}";

        IAsyncDisposable? primeira = await locking.TryAcquireAsync(nome);
        Assert.NotNull(primeira);
        await primeira.DisposeAsync();

        // Um bloqueio que não se largasse deixaria o trabalho parado para sempre depois da primeira
        // passagem, e isso é pior do que não haver bloqueio nenhum.
        await using IAsyncDisposable? segunda = await locking.TryAcquireAsync(nome);
        Assert.NotNull(segunda);
    }

    [Fact]
    public async Task Different_jobs_do_not_block_each_other()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IJobLock locking = scope.ServiceProvider.GetRequiredService<IJobLock>();

        // O bloqueio é por trabalho, não por worker: a fila de IA e o varrimento das integrações
        // podem correr ao mesmo tempo em máquinas diferentes, e devem.
        await using IAsyncDisposable? um = await locking.TryAcquireAsync($"ensaio-a-{Guid.NewGuid():N}");
        await using IAsyncDisposable? outro = await locking.TryAcquireAsync($"ensaio-b-{Guid.NewGuid():N}");

        Assert.NotNull(um);
        Assert.NotNull(outro);
    }
}
