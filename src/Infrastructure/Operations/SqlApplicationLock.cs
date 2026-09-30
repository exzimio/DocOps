using DocOps.Application.Abstractions;
using DocOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Operations;

/// <summary>
/// Exclusão mútua entre processos, através do bloqueio aplicacional do SQL Server.
///
/// O worker corre num processo à parte e nada impedia que houvesse dois. Dois worker a correr o
/// mesmo trabalho não é apenas desperdício: a fila de IA analisaria o mesmo documento duas vezes,
/// as integrações varreriam a mesma caixa de correio em duplicado, e a verificação de retenção
/// poderia anunciar o mesmo documento a duas velocidades diferentes. Nenhum desses casos dá erro;
/// dão trabalho repetido, que é pior porque não se vê.
///
/// A alternativa habitual é eleição de líder, com um serviço de coordenação à parte. Aqui não é
/// preciso: a base de dados já é o ponto comum a todos os processos, e <c>sp_getapplock</c> dá
/// exatamente a primitiva necessária. É barato, não acrescenta infraestrutura nenhuma, e desaparece
/// sozinho se a ligação cair, porque o bloqueio pertence à sessão.
///
/// <b>Sem espera, de propósito.</b> Tempo limite zero: se outro worker está a correr este trabalho,
/// este desiste e volta no ciclo seguinte. Esperar seria juntar dois worker no mesmo instante em vez
/// de os manter separados.
/// </summary>
public sealed class SqlApplicationLock(DocOpsDbContext dbContext, ILogger<SqlApplicationLock> logger)
    : IJobLock
{
    /// <summary>
    /// Tenta tomar o bloqueio com este nome. Devolve <c>null</c> quando outro processo o tem, e
    /// nesse caso quem chamou deve simplesmente não fazer o trabalho.
    /// </summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken = default)
    {
        string? connectionString = dbContext.Database.GetConnectionString();

        if (connectionString is null)
        {
            // Sem cadeia de ligação não há bloqueio a tomar. Não é motivo para parar o trabalho:
            // é motivo para o correr sem exclusão, como acontecia antes de isto existir.
            logger.LogWarning("Sem cadeia de ligação: o trabalho '{Name}' corre sem exclusão mútua", name);

            return NullHandle.Instance;
        }

        SqlConnection connection = new(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            // O bloqueio pertence à sessão, e não a uma transação: os trabalhos gravam por sua
            // conta e envolvê-los todos numa transação única seria mantê-la aberta durante minutos.
            await using SqlCommand command = new("sp_getapplock", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };

            command.Parameters.AddWithValue("@Resource", $"DocOps.Job.{name}");
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 0);

            SqlParameter result = command.Parameters.Add("@Result", System.Data.SqlDbType.Int);
            result.Direction = System.Data.ParameterDirection.ReturnValue;

            await command.ExecuteNonQueryAsync(cancellationToken);

            // 0 concedido de imediato, 1 concedido depois de esperar. Qualquer valor negativo é
            // recusa: -1 tempo esgotado, -2 cancelado, -3 vítima de impasse, -999 erro de chamada.
            int code = (int)result.Value;

            if (code >= 0)
            {
                return new Handle(connection, name, logger);
            }

            logger.LogDebug("O trabalho '{Name}' já está a correr noutro processo (código {Code})", name, code);
            await connection.DisposeAsync();

            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Handle(SqlConnection connection, string name, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                // Largar explicitamente em vez de confiar no fecho da ligação: com agrupamento de
                // ligações, fechar devolve-a ao conjunto sem a reiniciar de imediato, e o bloqueio
                // podia sobreviver à sua própria utilidade.
                await using SqlCommand command = new("sp_releaseapplock", connection)
                {
                    CommandType = System.Data.CommandType.StoredProcedure
                };

                command.Parameters.AddWithValue("@Resource", $"DocOps.Job.{name}");
                command.Parameters.AddWithValue("@LockOwner", "Session");

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception exception)
            {
                // Largar o bloqueio não pode derrubar o ciclo: fechar a ligação larga-o na mesma.
                logger.LogWarning(exception, "Falhou largar o bloqueio do trabalho '{Name}'", name);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>Um bloqueio que não bloqueia nada, para quando não há base de dados a que o pedir.</summary>
    private sealed class NullHandle : IAsyncDisposable
    {
        public static readonly NullHandle Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
