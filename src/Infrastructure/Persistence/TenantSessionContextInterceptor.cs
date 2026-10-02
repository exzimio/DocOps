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
/// de outra. O guarda que impede esse padrão de voltar é bom, mas trata aquele padrão: SQL escrito
/// à mão dentro da aplicação, um procedimento armazenado, ou um caminho novo que não passe pelo EF
/// não passam por nenhum dos dois. Passam pelo motor, e é aí que esta segunda parede está.
///
/// <b>O que ela não alcança:</b> quem se liga à base por fora da aplicação nunca escreve este
/// valor, e o predicado deixa passar quando ele está vazio. Uma ferramenta de relatórios ligada
/// diretamente vê todas as organizações. Fechar isso obrigaria a negar por omissão, com uma chave
/// de dispensa explícita para o worker e para a semeadura.
///
/// O valor é escrito em cada abertura de ligação, e não uma vez por processo, porque o agrupamento
/// de ligações entrega a mesma ligação física a pedidos de organizações diferentes. O
/// <c>sp_reset_connection</c> que o agrupamento dispara limpa o contexto de sessão, mas escrever
/// sempre torna isso irrelevante: a ligação sai daqui com o valor certo ou com nenhum.
/// </summary>
public sealed class TenantSessionContextInterceptor(
    ITenantContext? tenantContext,
    IDatabaseModeAccessor? modeAccessor = null) : DbConnectionInterceptor
{
    /// <summary>A chave com a organização, lida pelo predicado da política.</summary>
    public const string Key = "docops.tenant";

    /// <summary>
    /// A chave da dispensa. O predicado só a aceita quando a ligação também se faz com a identidade
    /// de base de dados própria: saber o nome da chave não chega para a obter.
    /// </summary>
    public const string SystemKey = "docops.system";

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

        bool sistema = modeAccessor?.Mode == DatabaseMode.System;

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "EXEC sp_set_session_context @key = N'" + Key + "', @value = @tenant, @read_only = 1;" +
            "EXEC sp_set_session_context @key = N'" + SystemKey + "', @value = @system, @read_only = 1;";

        DbParameter organizacao = command.CreateParameter();
        organizacao.ParameterName = "@tenant";

        // Em modo de sistema a organização fica por escrever de propósito: quem corre acima de todas
        // não tem uma, e escrever uma seria restringir o que não se quer restringido.
        organizacao.Value = sistema
            ? DBNull.Value
            : (object?)tenantContext?.TenantIdOrNull ?? DBNull.Value;
        command.Parameters.Add(organizacao);

        DbParameter dispensa = command.CreateParameter();
        dispensa.ParameterName = "@system";
        dispensa.Value = sistema ? 1 : DBNull.Value;
        command.Parameters.Add(dispensa);

        // Só de leitura: depois de posto, ninguém o muda no meio da sessão. Se alguém conseguisse
        // reescrever esta chave, a política passaria a filtrar pela organização que essa pessoa
        // escolhesse, e a segunda parede deixava de ser parede.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
