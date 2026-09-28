# Modelo de segurança

Este documento descreve os controlos implementados, os que estão planeados e as suposições em que
assentam. É um projeto de demonstração: não foi submetido a auditoria de segurança independente nem
se destina a tratar dados reais.

## 1. Princípios

1. **Nunca confiar no cliente.** Identificador de organização, identificador de utilizador e
   permissões vêm da sessão do servidor, nunca do pedido.
2. **Defesa em profundidade.** Cada garantia importante tem mais do que um ponto de aplicação.
3. **Falhar de forma fechada.** Na dúvida, negar e registar.
4. **Provar o que aconteceu.** Ações relevantes produzem registo de auditoria imutável.

## 2. Autenticação

| Controlo | Implementação |
| --- | --- |
| Mecanismo | ASP.NET Core Identity com cookie de sessão |
| Cookie | `docops.session`, `HttpOnly`, `SameSite=Lax`, `Secure` conforme o esquema do pedido |
| Armazenamento no cliente | Nenhum. Não há *token* acessível a JavaScript (sem `localStorage`, sem `sessionStorage`) |
| Palavra-passe | Mínimo 12 caracteres, maiúscula, minúscula, dígito e símbolo; hash PBKDF2 do Identity |
| Bloqueio | 5 tentativas falhadas → 15 minutos de bloqueio, aplicável também a contas novas |
| Enumeração de contas | Palavra-passe errada e conta inexistente devolvem exatamente a mesma resposta |
| Revalidação | *Security stamp* revalidado a cada 5 minutos: permissões revogadas deixam de funcionar sem esperar pelo fim da sessão |
| Sessão | 8 horas com renovação deslizante |

Em produção, o cookie deve ser servido apenas sobre HTTPS (`CookieSecurePolicy.Always`) e a
aplicação deve estar atrás de HSTS.

## 3. Autorização

- Os *endpoints* exigem **permissões**, não perfis: `[HasPermission(Permissions.DocumentsApprove)]`.
- As permissões são *claims* de perfil guardadas em `AspNetRoleClaims` — administráveis sem novo
  *deploy* e auditáveis como qualquer outra alteração.
- O `PermissionPolicyProvider` gera políticas a pedido; acrescentar uma permissão não exige alterar
  o arranque da aplicação.
- O frontend esconde o que o utilizador não pode usar, mas essa decisão **não é** um controlo de
  segurança: a mesma regra é aplicada no servidor em todos os casos.
- A autorização por recurso (documento) combina propriedade, permissões explícitas a utilizador,
  departamento ou perfil, e a classificação de sensibilidade. O resultado é intersetado com as
  permissões do perfil na organização: partilhar um documento com um colaborador nunca o transforma
  em aprovador. O painel de *acesso efetivo* mostra a razão de cada regra aplicada.
- Utilizadores externos (fornecedores, parceiros) nunca obtêm acesso por classificação: só por
  permissão explícita.
- A lista de documentos aplica as regras de acesso **dentro da consulta SQL**, para que a contagem
  total e a paginação não revelem a existência de documentos que o utilizador não pode ver.
- As tarefas seguem a mesma lógica: uma tarefa é visível a quem a tem atribuída e a quem pertence ao
  departamento ou perfil a que foi oferecida. Agir sobre ela exige `tasks.act`, e reatribuir exige
  `tasks.reassign` — ver uma tarefa nunca basta para a decidir.
- As ações disponíveis numa tarefa são calculadas no servidor e enviadas com ela; a interface
  apresenta o que lhe é dito, e cada *endpoint* volta a verificar a mesma regra antes de agir.

## 4. Isolamento entre organizações

| Camada | Controlo |
| --- | --- |
| Sessão | O identificador da organização existe apenas como *claim*; nenhuma rota o aceita como parâmetro |
| Leitura | Filtros globais de consulta em todas as entidades com organização |
| Escrita | `TenantGuardInterceptor` atribui a organização a novas linhas e recusa escritas cruzadas |
| Testes | Testes de integração verificam ambos os sentidos com duas organizações reais em base de dados |

## 5. Proteção contra CSRF

O cookie é `SameSite=Lax`, o que impede o browser de o enviar em pedidos `POST` originados noutro
sítio. Adicionalmente, `CookieOriginValidationMiddleware` recusa qualquer método que altere estado
(`POST`, `PUT`, `PATCH`, `DELETE`) cujo `Origin` — ou, na ausência deste, o `Referer` — não seja a
própria aplicação ou uma origem explicitamente autorizada. A resposta é `403` com
`code: origin_not_allowed` e fica registada.

Esta abordagem protege toda a superfície da API sem exigir gestão de *tokens* antifalsificação no
cliente.

## 6. Cabeçalhos de segurança

| Cabeçalho | Valor |
| --- | --- |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` |
| `Cross-Origin-Resource-Policy` | `same-site` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` |

A API serve exclusivamente JSON, pelo que a CSP pode ser máxima. A CSP da aplicação web é definida
no alojamento estático do frontend.

## 7. Limitação de tráfego

| Âmbito | Limite |
| --- | --- |
| Global | 300 pedidos por minuto por utilizador autenticado ou endereço IP |
| Autenticação | 10 tentativas por 5 minutos por endereço IP |

Excedido o limite, a resposta é `429`. A limitação por IP complementa — não substitui — o bloqueio de
conta do Identity, que é por conta e, por isso, imune à partilha de endereço.

Os limites são configuráveis (secção `RateLimiting`) porque num ambiente com endereço partilhado —
escritório atrás de NAT, *runner* de CI, máquina local a correr toda a bateria de testes — um limite
por IP penaliza utilizadores legítimos. O ambiente de desenvolvimento eleva o limite de autenticação
para 100; produção usa as predefinições.

## 8. Validação de entrada

- Validação com FluentValidation aplicada por um filtro global: nenhum controlador pode esquecer-se
  de validar.
- Erros devolvem `400` em `ProblemDetails` com `code: validation_failed` e os erros por campo.
- O comprimento da palavra-passe é limitado antes do cálculo de *hash*, o que evita trabalho
  criptográfico desnecessário com cargas grandes.
- Carregamento de ficheiros: lista branca de extensões e de tipos de conteúdo, dimensão máxima
  configurável, nome de ficheiro higienizado e chave de armazenamento gerada pelo servidor — um nome
  de ficheiro manipulado não consegue escapar da raiz de armazenamento, e o caminho resolvido é
  validado contra essa raiz antes de qualquer leitura ou escrita.

## 9. Auditoria

- Registo imutável em `AuditEvents`: a aplicação lança exceção em qualquer tentativa de alteração ou
  eliminação.
- O ator é determinado pelo servidor, não pelo chamador.
- Escrita em contexto próprio: o registo sobrevive ao *rollback* da operação que descreve.
- Campos com `password`, `secret`, `token`, `apikey` ou `connectionstring` no nome são redigidos
  antes de serem serializados.

Em produção, complemente com uma restrição ao nível da base de dados:

```sql
DENY UPDATE, DELETE ON dbo.AuditEvents TO [docops_app];
```

## 10. Registos e dados sensíveis

- Os logs nunca contêm palavras-passe, cookies nem cadeias de ligação.
- Os *health checks* devolvem apenas nome, estado e duração de cada verificação; nenhuma mensagem de
  exceção, nome de servidor ou cadeia de ligação é exposta.
- As respostas de erro nunca incluem *stack traces*: o cliente recebe um identificador de correlação
  que permite localizar o detalhe nos logs do servidor.

## 11. Gestão de segredos

- O repositório não contém segredos de produção.
- A única credencial versionada é a do container SQL Server de desenvolvimento, ligado apenas a
  `127.0.0.1` e documentada como tal.
- Em desenvolvimento, use *user secrets* ou `.env` (ambos ignorados pelo Git); em produção, use as
  variáveis de ambiente da plataforma ou um cofre de segredos.

## 12. Ameaças consideradas

| Ameaça | Mitigação |
| --- | --- |
| Roubo de sessão por XSS | Cookie `HttpOnly`; nenhum *token* acessível a JavaScript |
| CSRF | `SameSite=Lax` + validação de origem em métodos que alteram estado |
| Força bruta e *credential stuffing* | Bloqueio de conta + limitação por IP + respostas indistinguíveis |
| Enumeração de contas | Resposta idêntica para conta inexistente e palavra-passe errada |
| Fuga entre organizações | Filtros de consulta + intercetor de escrita + testes dedicados |
| Acesso indevido a documento | Avaliação por recurso no servidor, com teto pelas permissões do perfil |
| Travessia de caminho no armazenamento | Chave gerada pelo servidor e caminho validado contra a raiz |
| Carregamento de ficheiro perigoso | Lista branca de extensões e tipos, limite de dimensão, nome higienizado |
| Decisão de aprovação por quem não devia | Autorização por tarefa no servidor, verificada em cada ação |
| Execução de código a partir de dados guardados | As condições de workflow são um operador de conjunto fechado sobre uma variável, não uma linguagem de expressões |
| Processo a contornar as regras do documento | As alterações de estado configuradas num nó passam pela máquina de estados do documento |
| Desenho de processo a contornar a validação | Desenhar exige `workflows.design` e ativar exige `workflows.activate`; a validação que decide a ativação corre no servidor, sobre o que está gravado |
| Configuração de nó arbitrária vinda do cliente | O JSON é analisado no servidor contra o modelo de configuração conhecido e regravado normalizado; o que não pertence ao tipo do nó não sobrevive à gravação |
| Escrita perdida em edição concorrente | O `rowversion` da definição é sempre verificado ao guardar, mesmo quando só o grafo mudou |
| Processo em curso alterado debaixo dos pés | Guardar uma definição com execuções cria uma versão nova; as instâncias continuam a resolver os nós na versão com que arrancaram |
| Resposta inventada apresentada como facto | Toda a citação é confirmada contra o texto do documento; o que não confere é descartado e contado, e uma resposta sem suporte não é apresentada |
| Valor inventado por um modelo | Além da citação, o valor tem de se poder reler a partir das palavras citadas; caso contrário não chega a ser sugerido |
| Demonstração confundida com um modelo real | A origem (`Demo`/`Model`) faz parte do contrato da API e é mostrada onde quer que o resultado apareça |
| IA a escrever no documento sem supervisão | Nenhuma sugestão é aplicada sem decisão humana; cada decisão é auditada e a escrita passa pela mesma validação da edição manual |
| Conteúdo confidencial enviado para um modelo | O fornecedor de modelo é opcional e desligado por omissão; a chave vem só do ambiente, o corpo do pedido leva apenas o texto do documento, e o texto enviado é limitado por configuração |
| Fuga de informação por contagens | A fila e as estatísticas de IA contam apenas análises de documentos que o utilizador pode abrir |
| Gramática de texto integral usada como entrada | O texto escrito nunca chega à condição como sintaxe: é separado em termos e a condição é reconstruída com cada termo entre aspas, pelo que `OR`, `NEAR` ou uma aspa solta viajam como palavras |
| Pergunta em linguagem natural a alcançar mais do que o formulário | O leitor devolve filtros, não resultados; quem executa é o mesmo serviço de pesquisa, com o mesmo predicado de autorização |
| Modelo a nomear dados que não viu | Os nomes que o modelo devolve são reconferidos contra o catálogo da própria organização; o que lá não está é descartado, e identificadores nunca lhe são enviados |
| Fuga de informação pela contagem de resultados | O predicado de permissões faz parte da consulta: o total é o total que quem pergunta pode conhecer, não o que existe |
| Excerto a realçar o que o documento não diz | A posição do realce vem do servidor, que a encontrou no texto guardado; a interface não volta a procurar e recusa um intervalo que não caiba no excerto |
| Exposição da lista de pessoas da organização | `/api/tenants/current/users` devolve nome e departamento, nada mais, só a quem já pode ver documentos, e apenas da própria organização |
| Correio de uma organização visível noutra | Filtro de consulta por organização em `EmailMessage` e `EmailAttachment` — e um teste que enumera todas as entidades com `TenantId` e falha se alguma ficar sem filtro |
| Entrada de documentos por porta lateral | O que a caixa de entrada produz passa pelo `IDocumentService` e pelo `IWorkflowService`, com a validação, as permissões e a auditoria de sempre |
| Associar uma mensagem a um documento que o utilizador não pode ver | O documento é carregado e verificado com `DocumentAccessLevel.View` antes de a ligação ser gravada; caso contrário a referência e o título apareceriam no ecrã de quem não lhes pode aceder |
| Simulação de correio confundida com uma caixa ligada | A origem (`Demo`/`Microsoft365`) faz parte do contrato da API e é mostrada onde quer que as mensagens apareçam |
| Integração a alterar o sistema de origem | O conetor só lê: não escreve na caixa, não envia e não apaga |
| Mensagens duplicadas por sincronizações repetidas | Índice único `(TenantId, Mailbox, ExternalId)`; sincronizar de novo reconhece o que já existe em vez de o acrescentar |
| Ação oferecida a quem o servidor vai recusar | As ações da caixa de entrada só aparecem com `inbox.process`, e o estado da própria mensagem (`canAct`, `canArchive`, `canReopen`) viaja no contrato para a interface espelhar as regras que o servidor aplica |
| **Pedido forjado do lado do servidor (SSRF) pelo destino de um webhook** | O destino é validado **no servidor e antes de ser guardado**: só HTTPS, e recusam-se *loopback* e as gamas privadas. O diálogo do browser valida também, mas só para a mensagem — a verificação que conta é a que o cliente não pode contornar |
| Credencial de terceiros exposta pelo ecrã de integrações | As definições de uma integração são explicitamente **não secretas**: nenhuma credencial é guardada em `SettingsJson`, e é isso que torna seguro mostrar o que lá está |
| Quem só pode observar a alterar uma integração | Ver e mudar são permissões distintas: `integrations.view` mostra cartões e registos, `integrations.manage` é exigida para executar, ativar, desativar e configurar — no servidor, em cada ação |
| Integração de uma organização alcançada por outra | O serviço do pedido usa o `DbContext` filtrado pela organização: uma integração de outra organização não é encontrada, e um pedido por identificador devolve `404` em vez de admitir que existe |
| Estado de integração inventado pelo próprio adaptador | Os contadores e o estado são escritos pelo executor a partir do resultado devolvido, nunca pelo adaptador — um adaptador não se pode declarar «Ligada» sem ter feito nada |
| Interior do servidor exposto pela falha de uma integração | A exceção de um adaptador vira uma linha de registo com a **mensagem**, nunca a pilha |
| Trabalho simulado apresentado como real, ou trabalho real apresentado como simulação | `IsSimulated` é declarado adaptador a adaptador e o aviso diz **o que** é simulado; os três adaptadores que fazem trabalho verdadeiro não se rotulam de demonstração |
| **Utilizador externo a ver documentos que não são seus** | O portal usa o mesmo avaliador de acesso da aplicação interna, que concede a um externo apenas o que ele possui ou lhe foi explicitamente concedido — nunca por classificação. O portal não tem regras próprias que pudessem divergir |
| Nota interna a chegar a um fornecedor por um DTO partilhado | Os contratos do portal são tipos próprios, escritos à mão. Um campo acrescentado a um DTO interno não é servido ao portal porque não é o mesmo tipo |
| Evento interno novo a aparecer no portal por omissão | A conversa é construída selecionando dois tipos de evento pelo nome, nunca por exclusão: uma lista por exclusão falha aberta |
| Fornecedor a aprovar a sua própria fatura | Responder a um pedido de informação acrescenta informação e não move o processo; a decisão é de quem perguntou |
| Mensagens não solicitadas de um externo no processo | Só se responde a um pedido de informação em aberto; sem pergunta, o servidor recusa com `409` |
| Utilizador interno a usar o portal, ou externo a usar a aplicação | Nenhum perfil interno detém `portal.access`, nem o de administrador; a reencaminhação no cliente é conveniência, a recusa é do servidor |
| **Assinatura apresentada como juridicamente válida** | O aviso de que é uma demonstração sem validade legal aparece no topo do painel de assinatura, não em rodapé; nenhuma resposta da API alega o contrário (ADR-009) |
| Assinar em nome de outra pessoa | Só o signatário nomeado assina, comparado por identificador no servidor. Nenhuma permissão, nem a de administrador da organização, contorna a regra |
| Assinatura re-apontada para conteúdo que ninguém consentiu | O pedido fixa a versão e o resumo do conteúdo; assinar é recusado se divergirem, e uma versão posterior faz o registo deixar de corresponder em vez de o reescrever |
| Direitos de edição distribuídos só para recolher assinaturas | Assinar exige ver o documento, não editá-lo: a transição de estado é validada pelo ciclo de vida, não pelos direitos de quem age |
| Pedido de assinatura que ninguém pode cumprir | Recusado ao pedir, quer o signatário não veja o documento, quer não tenha permissão para assinar |
| Dúvida sobre se um documento está assinado | No máximo um pedido em aberto por documento, garantido por índice único filtrado na base de dados |
| Fuga de informação pelos números de um painel | As consultas do relatório passam pelo mesmo predicado de visibilidade da listagem e da pesquisa: um total conta o que quem pergunta pode abrir. Saber que existem catorze documentos confidenciais no jurídico é saber alguma coisa sobre eles |
| Exportação de dados por quem só pode consultar | Ver e exportar são permissões distintas: `reports.view` mostra o painel, `reports.export` é exigida para o CSV — no servidor, não apenas escondendo o botão |
| **Administrador de uma organização a gerir a conta que administra a plataforma** | `IsPlatformAdministrator` exclui a conta da edição e da desativação, no DTO **e** no serviço. Sem isto, guardar uma edição demovia-a em silêncio, porque `SystemAdmin` não consta dos perfis que o ecrã oferece |
| **Organização a promover-se para fora da sua fronteira** | `SystemAdmin` não é devolvido no catálogo de perfis atribuíveis **e** é recusado com `409` se o nome for enviado à mão. A primeira metade sozinha seria apenas uma interface discreta |
| Administrador a trancar a organização fora de si própria | A própria conta não se edita nem se desativa a partir daqui: retirar-se o perfil de administração é a única avaria sem reparação possível de dentro do produto |
| Conta eliminada a apagar o significado da auditoria | Contas são desativadas, nunca eliminadas: os documentos, as decisões e o trilho referem-se àquela conta |
| Palavra-passe inicial recuperável | Gerada com `RandomNumberGenerator` e devolvida **uma única vez**, na resposta à criação. O servidor guarda o resumo; não há endereço que a devolva |
| Administração de uma organização alcançada por outra | A organização vem das *claims*, nunca do pedido; uma conta de outra organização não é encontrada, e responde `404` em vez de admitir que existe |
| Administrar pessoas a dar por arrasto o catálogo | `admin.users` e `admin.documenttypes` são permissões distintas, verificadas no servidor em cada ação: manter o esquema de classificação é outro trabalho que decidir quem tem conta |
| Catálogo de uma organização alcançado por outra | O serviço usa o `DbContext` filtrado pela organização: um tipo de outra não é encontrado, e responde `404` em vez de admitir que existe |
| Metadados de documentos apagados pela porta da configuração | Um campo com valores não se elimina nem muda de tipo: eliminar apagaria o que os documentos dizem, e mudar de tipo deixaria os valores na coluna errada — ilegíveis, não em erro |
| Catálogo a afirmar o que os documentos não cumprem | Tornar um campo obrigatório é recusado se algum documento do tipo ficasse por preencher, com a contagem na resposta |
| Entrada de documentos fechada por configuração | O tipo de entrada não se desativa nem aceita campos obrigatórios: fechá-lo travaria o correio e o portal, onde ninguém está a olhar para reparar |
| **Filtros de organização desligados sem querer por uma subconsulta** | `IgnoreQueryFilters()` é um operador de consulta: dentro de uma projeção desliga os filtros da consulta toda, incluindo o da organização, sem falhar nada. Aconteceu na lista de etiquetas. Uma guarda lê o código-fonte e falha a compilação se o operador aparecer dentro de uma projeção |
| Etiquetas de uma organização visíveis noutra | Contagens em consulta própria, com a condição de organização escrita à mão, e um teste de integração que verifica identidade — nenhuma etiqueta de uma aparece na outra |
| Anotações de documentos destruídas por um clique | Remover uma etiqueta é reversível: as ligações sobrevivem e restaurar devolve o mesmo número de documentos. O que é irreversível — um campo de metadados com valores — continua a ser recusado |
| Retenção encurtada sem rasto | Mudar a duração é permitido, porque as obrigações mudam, mas fica na auditoria com o valor antigo e o novo: move a data de eliminação de todos os documentos que a política governa |
| Regra de retenção abandonada a continuar a aplicar-se | Retirar uma política é recusado enquanto um tipo de documento a adotar por omissão, e a mensagem nomeia os tipos |
| Quem classifica a decidir quanto tempo se guarda | `admin.documenttypes` gere etiquetas e tipos; alterar políticas de retenção exige `admin.settings` — conformidade e classificação são decisões diferentes |
| Explicação de acesso usada para sondar o alcance alheio | Perguntar pelo acesso de outra pessoa exige `Share` sobre o documento; sobre o próprio basta poder vê-lo. Quem não decide concessões não tem razão para conhecer o alcance dos outros |
| Painel de autorização a divergir da regra que aplica | O painel não calcula: mostra o `EffectiveAccess` produzido pelo mesmo avaliador que decide quando alguém abre, edita ou descarrega |
| Perfil inexistente ou de plataforma nomeado numa concessão | Os perfis oferecidos vêm de `RoleNames`, a lista que a autorização usa, e `SystemAdmin` é excluído: está acima da organização |
| Acesso temporário que nunca acaba | As concessões aceitam prazo, e o predicado de visibilidade respeita-o; um prazo no passado é recusado na validação |
| Escalada de privilégios | Autorização por permissão no servidor; *claims* do cookie assinadas e revalidadas |
| Injeção SQL | EF Core com consultas parametrizadas; sem concatenação de SQL |
| Adulteração de auditoria | Intercetor de imutabilidade + revogação de permissões na base de dados |
| Divulgação em mensagens de erro | `ProblemDetails` uniformes, sem detalhe interno |

## 13. Trabalho em segundo plano

O worker corre sem organização no contexto ambiente, o que lhe permite processar todas as
organizações. É uma exceção deliberada: o processo não expõe superfície HTTP, os seus trabalhos são
escalonamento de prazos, retoma de temporizadores, fila de IA e varrimento das integrações, e cada
escrita continua a passar pelos mesmos intercetores de auditoria e de isolamento.

O caminho que ignora os filtros de organização está confinado a esses serviços de worker
(`IntegrationRunner`, e não `IntegrationService`, que é o que corre no pedido do utilizador). A
distinção é o que impede que um pedido HTTP alcance, por via de uma integração, dados que a sua
sessão não alcançaria diretamente.

## 14. Limitações conhecidas

- Sem autenticação multifator (fora do âmbito desta demonstração; a arquitetura de Identity suporta a
  adição).
- Sem rotação automática das chaves de proteção de dados em ambiente distribuído — em produção seria
  necessário um repositório de chaves partilhado.
- A assinatura eletrónica é uma demonstração funcional e **não tem qualquer validade legal**: não há
  chave privada sob controlo do signatário, nem certificado, nem carimbo temporal independente — os
  três elementos que distinguem uma assinatura avançada de um registo. Ver
  [ADR-009](adr/ADR-009-electronic-signature.md), que explica também porque não se gerou um par de
  chaves na aplicação: seria mais convincente e igualmente sem valor.
- O fornecedor de IA de demonstração é determinístico e está identificado como tal: nenhuma resposta
  de demonstração é apresentada como tendo sido gerada por um modelo real.
