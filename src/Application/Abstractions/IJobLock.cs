namespace DocOps.Application.Abstractions;

/// <summary>
/// Exclusão mútua entre processos, para um trabalho periódico.
///
/// O worker corre num processo à parte e nada impede que haja dois. Dois a correr o mesmo trabalho
/// não dá erro nenhum: dá trabalho repetido, que é pior porque não se vê. A fila de IA analisaria o
/// mesmo documento duas vezes e as integrações varreriam a mesma caixa de correio em duplicado.
///
/// É uma abstração e não uma classe de SQL porque isto é uma <i>política</i> — «um de cada vez» —
/// e não um detalhe do motor. Quem a implementa é que decide como: hoje é o bloqueio aplicacional
/// do SQL Server, que aproveita a base de dados que já é o ponto comum a todos os processos e não
/// acrescenta infraestrutura nenhuma.
/// </summary>
public interface IJobLock
{
    /// <summary>
    /// Tenta tomar o bloqueio com este nome, <b>sem esperar</b>. Devolve <c>null</c> quando outro
    /// processo o tem, e nesse caso quem chamou deve simplesmente não fazer o trabalho: esperar
    /// seria juntar dois worker no mesmo instante em vez de os manter separados.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Um bloqueio que concede sempre, para onde não há vários processos a coordenar.
///
/// Existe para que a ausência de coordenação seja uma escolha declarada e visível no registo de
/// serviços, em vez de um <c>null</c> a ser verificado em cada chamada.
/// </summary>
public sealed class NoJobLock : IJobLock
{
    private static readonly IAsyncDisposable Granted = new Nothing();

    public Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable?>(Granted);

    private sealed class Nothing : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
