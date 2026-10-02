using DocOps.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.Infrastructure.Persistence;

/// <summary>
/// O modo deste âmbito. Começa em <see cref="DatabaseMode.Tenant"/> e só muda por quem abre um
/// âmbito de sistema de propósito.
/// </summary>
public sealed class DatabaseModeAccessor : IDatabaseModeAccessor
{
    public DatabaseMode Mode { get; private set; } = DatabaseMode.Tenant;

    /// <summary>
    /// Interno por desenho: só <see cref="SystemScopeFactory"/> o chama, e logo a seguir a criar o
    /// âmbito, antes de qualquer ligação abrir. Se isto fosse público, alguém acabaria por mudar o
    /// modo a meio de um pedido, e o contexto da sessão já estaria escrito e só de leitura: o código
    /// julgaria ter dispensa e a base continuaria a filtrar, o que é a pior combinação possível.
    /// </summary>
    internal void ElevateToSystem() => Mode = DatabaseMode.System;
}

/// <inheritdoc />
public sealed class SystemScopeFactory(IServiceScopeFactory scopeFactory) : ISystemScopeFactory
{
    public ISystemScope Create()
    {
        IServiceScope scope = scopeFactory.CreateScope();

        // Elevado antes de devolver, portanto antes de qualquer ligação deste âmbito abrir.
        ((DatabaseModeAccessor)scope.ServiceProvider.GetRequiredService<IDatabaseModeAccessor>())
            .ElevateToSystem();

        return new SystemScope(scope);
    }

    private sealed class SystemScope(IServiceScope scope) : ISystemScope
    {
        public IServiceProvider Services => scope.ServiceProvider;

        public void Dispose() => scope.Dispose();

        public ValueTask DisposeAsync() =>
            scope is IAsyncDisposable assincrono ? assincrono.DisposeAsync() : Descartar();

        private ValueTask Descartar()
        {
            scope.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
