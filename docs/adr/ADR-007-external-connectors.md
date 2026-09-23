# ADR-007 — Sistemas externos atrás de adaptadores, com implementação de demonstração sem credenciais

- **Estado:** aceite (implementação na Fase 6)
- **Data:** 2026-09-22
- **Âmbito:** caixa de entrada de correio, centro de integrações

## Contexto

O briefing pede duas coisas que tocam sistemas de terceiros: uma caixa de entrada documental com
«camada de abstração para uma futura integração com Outlook / Microsoft Graph», explicitamente **sem
exigir credenciais da Microsoft**, e um centro de integrações com «adaptadores/interfaces para que
possam mais tarde ser ligados a APIs reais».

O problema é o mesmo nos dois casos, e é o mesmo que a Fase 5 resolveu para a IA: a funcionalidade é
central no produto, mas não pode ser um requisito para executar, demonstrar ou testar o projeto.
Quem avalia este repositório tem de conseguir ver o percurso completo — uma fatura chega por
correio, torna-se documento, entra num processo — sem subscrição, sem consentimento de tenant e sem
rede.

Há um segundo problema, menos óbvio. Uma simulação bem feita é indistinguível da coisa real no ecrã.
Se a caixa de entrada mostrar mensagens realistas sem dizer de onde vêm, quem vê o ecrã conclui que
há uma ligação a um servidor de correio. Isso é uma afirmação falsa sobre o que o sistema faz.

## Decisão

Todo o sistema externo entra por um **adaptador na camada de aplicação**, com pelo menos uma
implementação de demonstração que não precisa de credenciais nem de rede.

Para o correio:

```csharp
public interface IMailConnector
{
    MailConnectorInfo Info { get; }

    Task<IReadOnlyList<IncomingMail>> FetchAsync(
        string mailbox, DateTimeOffset? since, CancellationToken cancellationToken = default);

    Task<Stream> OpenAttachmentAsync(
        string messageId, string attachmentId, CancellationToken cancellationToken = default);
}
```

A forma é a do Microsoft Graph, de propósito: listar mensagens de uma caixa, depois buscar o
conteúdo de **um** anexo por identificador. Substituir `DemoMailConnector` por um conetor real é uma
classe nova e uma linha de registo, não um redesenho.

## Regras não negociáveis

- **Uma simulação diz que é uma simulação.** `MailConnectorInfo.Source` (`Demo` / `Microsoft365`)
  faz parte do contrato da API, viaja com cada mensagem e é mostrado no ecrã. A mesma regra do
  [ADR-004](ADR-004-ai-provider-abstraction.md) para a IA, aplicada ao correio.
- **Um conetor lê.** Não escreve na caixa, não envia e não apaga. Uma integração que só consegue ler
  é uma integração que não consegue estragar o sistema que lê.
- **Nenhuma credencial no repositório.** A implementação de demonstração não tem nenhuma para ter; a
  real lê-as do ambiente, como o fornecedor de IA.
- **O adaptador não é uma porta lateral.** O que entra pelo conetor passa pelos mesmos serviços de
  domínio que o resto: um anexo guardado como documento é criado pelo `IDocumentService`, com a
  mesma validação, a mesma referência, as mesmas permissões e a mesma auditoria de um carregamento
  manual.
- **Sincronizar é reconciliar, não acumular.** O identificador que o sistema de origem dá à mensagem
  é guardado e tem índice único por `(TenantId, Mailbox, ExternalId)`. Sincronizar duas vezes não
  acrescenta nada — e o botão vai ser carregado duas vezes.

## Alternativas consideradas

**Exigir Microsoft Graph desde o início.** Rejeitado pelo briefing e pelo bom senso: obrigaria a uma
subscrição do Microsoft 365, ao registo de uma aplicação no Entra ID e ao consentimento de um
administrador de tenant só para ver um ecrã funcionar. A abstração mantém essa porta aberta sem a
tornar obrigatória.

**Semear mensagens diretamente na base de dados.** Mais curto, e não provaria nada. O caminho que um
avaliador exercita ao carregar em «Sincronizar» seria diferente do caminho que produziu os dados que
está a ver. O semeador usa o mesmo conetor que a aplicação usa em execução, precisamente para que
não haja dois caminhos.

**Guardar os anexos todos na sincronização.** Rejeitado. Encheria o repositório com os anexos que
ninguém quis. Os bytes ficam onde o sistema de correio os tem até alguém decidir que o ficheiro vale
a pena; é também assim que o Graph funciona, por isso a abstração não tem de fingir.

**Um serviço separado por sistema externo (Outlook, ERP, CRM…).** Rejeitado nesta escala: seriam
seis formas diferentes de fazer a mesma coisa. Um adaptador por família de sistema, com contrato
declarado, chega — e é o que permite ao centro de integrações reportar todos da mesma maneira.

## Consequências

**Positivas**

- O projeto corre e demonstra-se sem conta em nenhum serviço de terceiros.
- O percurso de entrada de documentos é real de ponta a ponta, com dados realistas.
- Trocar a demonstração por um sistema real é uma implementação da interface, sem tocar no domínio.
- O que é simulado está dito onde aparece, e não onde só um programador o leria.

**Negativas**

- A implementação de demonstração é mais uma coisa a manter, e pode divergir do comportamento real
  de um sistema que nunca é exercitado.
- A forma do adaptador está enviesada pelo Graph. Um sistema de correio com um modelo muito
  diferente (IMAP puro, por exemplo) precisaria de ajustes no contrato.

**Sinais de revisão:** necessidade de escrever de volta no sistema de origem (marcar lido no
servidor, mover para uma pasta), ou um segundo conetor real cujo modelo não caiba nesta forma.

## Como ficou, na prática (Fase 6 — caixa de entrada)

Três pormenores que a decisão não antecipava.

### O identificador da mensagem não é único globalmente, e não precisa de ser

`DemoMailConnector` constrói o identificador a partir da parte local da caixa (`faturas`,
`contratos`, `geral`), pelo que duas organizações que monitorizem ambas um endereço `faturas@…`
recebem o mesmo identificador para a mesma correspondência semeada. Não é defeito: o que
desduplica é `(TenantId, Mailbox, ExternalId)`, e esses diferem sempre entre organizações. É também
como os sistemas de correio reais se comportam — um *message id* é único dentro de uma caixa, não no
universo. Está fixado num teste para que ninguém o «corrija» sem perceber porquê.

### O endereço das caixas vem do domínio da própria organização

As caixas monitorizadas são `faturas@`, `contratos@` e `geral@` sobre o domínio que as pessoas dessa
organização já usam no seu endereço, não sobre o *slug* do inquilino. É o domínio em que uma caixa
monitorizada realmente viveria, e mantém o semeador e a sincronização em execução a apontar para as
mesmas caixas em vez de criarem dois conjuntos lado a lado. Todos terminam em `.example`, que os RFC
reservam e que não resolve.

### O filtro de organização faltou, e a lição ficou num teste

A primeira versão desta funcionalidade não declarou o filtro de consulta por organização em
`EmailMessage` nem em `EmailAttachment` — e o administrador de uma organização via o correio da
outra. Os filtros são declarados **um a um, à mão** no `DocOpsDbContext`, porque diferem (um filho de
documento também desaparece quando o documento é apagado); isso torna cada entidade nova um buraco
até alguém se lembrar.

A correção pontual não chegava. `TenantFilterCoverageTests` passou a enumerar todas as entidades com
`TenantId` e a falhar, pelo nome, se alguma ficar sem filtro. As Fases 7 a 10 trazem `Integration`,
`IntegrationLog`, `SignatureRequest` e `LegalHold`, todas com organização — e agora nenhuma delas
pode chegar a *commit* sem filtro.

## Como ficou, na prática (Fase 6 — centro de integrações)

O centro de integrações estendeu esta decisão a mais cinco famílias de sistema — ERP, CRM,
assinatura eletrónica, webhooks e importação de ficheiros — através de `IIntegrationAdapter`, que é
o `IMailConnector` generalizado: um adaptador recebe um contexto com a organização e devolve um
resultado com o que fez, quantos registos tocou e as linhas que quer ver no registo. Quatro
pormenores que a decisão original não antecipava.

### «Simulada» é uma afirmação mais fina do que esta ADR supunha

A regra escrita acima — «uma simulação diz que é uma simulação» — pressupõe que os adaptadores se
dividem em dois campos: reais e fingidos. Com seis adaptadores escritos, não se dividem.

Só três têm um lado de lá inventado: o ERP e o CRM reconciliam contra listas fixas locais, e o
Outlook corre sobre o `DemoMailConnector`. Os outros três fazem trabalho verdadeiro de ponta a
ponta — a importação lê e move ficheiros reais do disco, os webhooks entregam HTTP real a um destino
externo, e a assinatura conta os documentos que estão mesmo à espera de assinatura nesta
organização. `IntegrationAdapterInfo.IsSimulated` é, por isso, declarado por adaptador e não pela
família, e a `SimulationNotice` diz **o que** é simulado, não que «isto é uma demonstração».

Marcar os seis como simulados teria sido uma mentira ao contrário: faria parecer teatro o que é
código a funcionar, que é o defeito que este projeto inteiro existe para não ter. A honestidade
corta nos dois sentidos — não alegar mais do que se faz, e não alegar menos.

### Os contadores vivem no executor, nunca nos adaptadores

Um adaptador devolve um `IntegrationRunResult`; quem escreve `Status`, `RecordsProcessed`,
`ErrorCount` e `LastRunAtUtc` na linha da integração é o `IntegrationExecutor`, a partir desse
resultado. A separação é deliberada: se cada adaptador escrevesse o seu próprio estado, um adaptador
podia declarar-se «Ligada» sem ter feito nada, e o cartão passaria a ser a opinião do adaptador
sobre si mesmo em vez do registo do que aconteceu. O executor também é quem apanha a exceção de um
adaptador e a converte em estado «Com falha» com a mensagem — nunca a pilha, que traria internos
para o ecrã.

### O nó de integração não chama nada dentro da transação do motor

Uma travessia do grafo é uma transação ([ADR-006](ADR-006-workflow-engine.md)). Uma chamada a um
sistema externo lá dentro manteria essa transação aberta enquanto se espera pela rede, e uma
integração lenta passaria a ser um problema de base de dados. `IntegrationNodeExecutor` enfileira um
`IntegrationRunRequest`, devolve `NodeResult.Wait()` e larga a transação; o worker corre o pedido e
chama `WorkflowEngine.ResumeAsync`. É a mesma forma que a Fase 5 deu aos nós de IA, replicada de
propósito: dois mecanismos diferentes para o mesmo problema seriam duas coisas para depurar.

Com isto, o nó `Integration` deixou de ser o último tipo que falhava de forma explícita, e o
dicionário `NotYetExecutable` do validador ficou vazio — todos os tipos de nó que o desenhador
oferece são agora executáveis.

### O destino do webhook é validado no servidor porque é uma superfície de SSRF

Um administrador a escrever um endereço que **este** servidor vai depois chamar é exatamente a forma
de um pedido forjado do lado do servidor. `WebhookEndpoint.IsAcceptable` exige HTTPS e recusa
*loopback* e as gamas privadas, e corre no `PUT` — antes de o endereço ser guardado, não só na
entrega. O diálogo do browser valida também, mas só para a mensagem; a verificação que conta é a que
o cliente não pode contornar.

Em troca, as definições de uma integração são explicitamente **não secretas**: nenhuma credencial é
guardada em `SettingsJson`. É essa decisão que permite ao cartão mostrar o endereço já configurado
sem expor nada — e sem ela, o formulário teria de fingir que não sabe o que guardou.
