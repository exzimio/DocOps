using System.Data.Common;
using DocOps.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocOps.Infrastructure.Persistence;

/// <summary>
/// Diz ao SQL Server a que organização pertence esta ligação.
///
/// É a metade de cima da defesa em profundidade contra fugas entre organizações. A metade de baixo
/// é a política de segurança ao nível da linha, que lê este valor e filtra as tabelas por ele; sem
/// alguém que o ponha na ligação, a política não sabe por quem filtrar.
///
/// <b>Porque é que isto existe, se já há filtros globais no EF Core.</b> Os filtros do EF são uma
/// defesa na camada de aplicação: protegem as consultas que passam pelo EF, e só enquanto ninguém
/// as desligar. Já falharam uma vez neste projeto — <c>IgnoreQueryFilters()</c> dentro de uma
/// projeção desligou-os para a consulta inteira e um administrador de uma organização viu etiquetas
/// de outra. O guarda que impede esse padrão de voltar é bom, mas trata aquele padrão. SQL escrito
/// à mão, uma ferramenta de relatórios ligada diretamente à base, ou um caminho novo que ninguém
/// previu, não passam por nenhum dos dois. Passam pelo motor, e é aí que esta segunda parede está.
///
/// O valor é escrito em cada abertura de ligação, e não uma vez por processo, porque o agrupamento
/// de ligações entrega a mesma ligação física a pedidos de organizações diferentes. O
/// <c>sp_reset_connection</c> que o agrupamento dispara limpa o contexto de sessão, mas escrever
/// sempre torna isso irrelevante: a ligação sai daqui com o valor certo ou com nenhum.
/// </summary>
public sealed class TenantSessionContextInterceptor(ITenantContext? tenantContext) : DbConnectionInterceptor
{
    /// <summary>A chave lida pela função de predicado da política de segurança.</summary>
    public const string Key = "docops.tenant";

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not SqlConnection)
        {
            return;
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'" + Key + "', @value = @tenant, @read_only = 1;";

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";

        // Sem organização no contexto — o worker, as migrações, a semeadura — o valor fica nulo, e
        // a política deixa passar tudo. É a mesma semântica dos filtros do EF, que também não
        // filtram quando não há organização: o trabalho periódico corre acima de todas, de propósito.
        parameter.Value = (object?)tenantContext?.TenantIdOrNull ?? DBNull.Value;
        command.Parameters.Add(parameter);

        // Só de leitura: depois de posto, ninguém o muda no meio da sessão. Se alguém conseguisse
        // reescrever esta chave, a política passaria a filtrar pela organização que essa pessoa
        // escolhesse, e a segunda parede deixava de ser parede.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
