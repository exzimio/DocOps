namespace DocOps.Application.Abstractions;

/// <summary>
/// Em que modo esta ligação à base de dados fala com o motor.
///
/// A política de segurança ao nível da linha filtra pela organização que encontra no contexto da
/// sessão. Mas há caminhos legítimos que correm **antes** de haver organização, ou **acima** de
/// todas: a autenticação procura a conta por e-mail sem saber a que organização pertence, o worker
/// corre para todas, e as migrações e a semeadura não pertencem a nenhuma.
///
/// Esses caminhos precisam de dispensa. O que não podem é tê-la por omissão, senão qualquer ligação
/// sem organização vê tudo — incluindo uma ferramenta de relatórios ligada diretamente à base, que
/// nunca escreve contexto nenhum.
///
/// Daí um modo **declarado**. O contexto da sessão fica só de leitura depois de escrito, portanto o
/// modo tem de ser decidido antes de a ligação abrir: não há como mudar a meio.
/// </summary>
public enum DatabaseMode
{
    /// <summary>
    /// O normal. A ligação vê apenas a organização que as claims do cookie identificam, e sem
    /// organização não vê nada.
    /// </summary>
    Tenant = 0,

    /// <summary>
    /// Dispensa declarada, para os caminhos que correm antes de haver organização ou acima de
    /// todas. Não basta pedi-la: a ligação também tem de se fazer com a identidade de base de dados
    /// que a política aceita, e essa não é a dos pedidos normais.
    /// </summary>
    System = 1
}

/// <summary>
/// O modo em vigor neste âmbito. É um serviço com tempo de vida de âmbito, não uma variável global:
/// dois pedidos a decorrer ao mesmo tempo têm modos independentes.
/// </summary>
public interface IDatabaseModeAccessor
{
    DatabaseMode Mode { get; }
}

/// <summary>
/// Abre um âmbito em modo de sistema.
///
/// Existe para que a dispensa seja **visível** no código que a usa. Uma procura de conta por e-mail
/// durante a autenticação é legítima; a mesma procura escondida no meio de um serviço qualquer não
/// é. Com isto, encontrar quem tem dispensa é procurar por quem chama este método.
/// </summary>
public interface ISystemScopeFactory
{
    /// <summary>
    /// Devolve um âmbito cujas ligações abrem em modo de sistema. Quem o chama resolve os serviços
    /// a partir dele, e descarta-o quando acabar.
    /// </summary>
    ISystemScope Create();
}

public interface ISystemScope : IDisposable, IAsyncDisposable
{
    IServiceProvider Services { get; }
}
