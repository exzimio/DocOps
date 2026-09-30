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

## 4.1 Segunda parede: segurança ao nível da linha

Os filtros globais do EF Core são uma defesa **na camada de aplicação**. Protegem as consultas que
passam pelo EF, e só enquanto ninguém os desligar. Já falharam uma vez neste projeto: o defeito 52
foi uma fuga verdadeira entre organizações, porque `IgnoreQueryFilters()` dentro de uma projeção
desliga os filtros da consulta inteira. Existe desde então um guarda que impede esse padrão de
voltar — mas um guarda trata o padrão que conhece. SQL escrito à mão não passa por ele. Uma
ferramenta de relatórios ligada diretamente à base também não.

Por baixo disso está uma **política de segurança ao nível da linha do SQL Server**, sobre as 26
tabelas que têm `TenantId`:

| Peça | O que faz |
| --- | --- |
| `TenantSessionContextInterceptor` | Escreve a organização em `SESSION_CONTEXT` a cada abertura de ligação, marcada como só de leitura |
| `security.fn_TenantAccessPredicate` | Função *inline* com `SCHEMABINDING`: entra no plano da consulta em vez de ser chamada linha a linha |
| `security.TenantIsolationPolicy` | Predicado de filtro nas 26 tabelas |

O valor é escrito **em cada abertura de ligação**, e não uma vez por processo, porque o agrupamento
de ligações entrega a mesma ligação física a pedidos de organizações diferentes. É marcado como só
de leitura para que ninguém o reescreva a meio da sessão: se conseguisse, a política passaria a
filtrar pela organização que essa pessoa escolhesse.

**Isto aplica-se a quem se liga como `sa`.** A segurança ao nível da linha não isenta
administradores, e é essa a diferença entre uma defesa na aplicação e uma defesa no motor. Foi
verificado desligando a política e voltando a ligá-la com a mesma sessão: uma organização visível
com ela ligada, duas com ela desligada.

**Sem organização no contexto, a política deixa passar.** É deliberado, e é a mesma semântica dos
filtros do EF: o worker corre acima de todas as organizações, e as migrações e a semeadura também. A
variante mais forte seria negar por omissão e exigir uma chave de dispensa explícita para esses
casos — fica por fazer, porque transformaria qualquer contexto em falta num ecrã vazio silencioso, e
essa troca merece ser feita com a história de implantação já construída.

**Só predicados de filtro, não de bloqueio.** A fuga que motivou isto era de leitura, e a atribuição
da organização na escrita já é feita pelo `TenantGuardInterceptor`. O predicado de bloqueio na
escrita é o passo seguinte natural.

`RowLevelSecurityTests` prova as quatro coisas que interessam: que `IgnoreQueryFilters()` deixou de
alcançar a outra organização, que SQL cru também não alcança, que **nenhuma tabela com `TenantId`
ficou sem predicado** — lido da própria base de dados, não do código — e que sem organização no
contexto tudo continua visível, para que mudar isso seja uma decisão e não um acidente.

## 5. Proteção contra CSRF

O cookie é `SameSite=Lax`, o que impede o browser de o enviar em pedidos `POST` originados noutro
sítio. Adicionalmente, `CookieOriginValidationMiddleware` recusa qualquer método que altere estado
(`POST`, `PUT`, `PATCH`, `DELETE`) cujo `Origin` — ou, na ausência deste, o `Referer` — não seja a
própria aplicação ou uma origem explicitamente autorizada. A resposta é `403` com
`code: origin_not_allowed` e fica registada.

**Um pedido sem `Origin` nem `Referer` é recusado, não aceite.** Vale a pena dizê-lo porque é a
falha habitual desta abordagem: uma verificação que só compara o `Origin` quando ele existe deixa
passar tudo o que o omitir. Aqui a origem tem de estar presente **e** ser permitida; se nenhum dos
dois cabeçalhos vier, a origem fica por determinar e o pedido leva `403`. Só os métodos que não
alteram estado (`GET`, `HEAD`, `OPTIONS`, `TRACE`) passam sem verificação.

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
- **Os primeiros bytes têm de corresponder à extensão.** A extensão e o tipo de conteúdo vêm ambos
  do cliente e forjam-se os dois: um executável chamado `relatorio.pdf` e declarado como
  `application/pdf` passava nas duas verificações anteriores. `FileSignatures` confere a assinatura
  do formato (`%PDF`, `PK` para os formatos Office modernos, OLE2 para os antigos, e assim por
  diante); para os formatos de texto, que não têm assinatura, a regra é a inversa — não podem
  começar por nenhuma assinatura binária conhecida nem conter bytes nulos.
- **O tipo de conteúdo guardado é derivado da extensão verificada, não o que o cliente declarou.**
  Depois de os bytes confirmarem o formato, a declaração do cliente deixa de ter utilidade, e
  continuar a guardá-la seria manter entrada não confiável a atravessar o sistema até ao cabeçalho
  de uma resposta. Os descarregamentos são servidos como anexo, e `X-Content-Type-Options: nosniff`
  impede o browser de adivinhar por sua conta.

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
| **Filtros de organização desligados sem querer por uma subconsulta** | `IgnoreQueryFilters()` é um operador de consulta: dentro de uma projeção desliga os filtros da consulta toda, incluindo o da organização, sem falhar nada. Aconteceu na lista de etiquetas. Uma guarda lê o código-fonte e falha a suite de testes se o operador aparecer dentro de uma projeção |
| Etiquetas de uma organização visíveis noutra | Contagens em consulta própria, com a condição de organização escrita à mão, e um teste de integração que verifica identidade — nenhuma etiqueta de uma aparece na outra |
| Anotações de documentos destruídas por um clique | Remover uma etiqueta é reversível: as ligações sobrevivem e restaurar devolve o mesmo número de documentos. O que é irreversível — um campo de metadados com valores — continua a ser recusado |
| Retenção encurtada sem rasto | Mudar a duração é permitido, porque as obrigações mudam, mas fica na auditoria com o valor antigo e o novo: move a data de eliminação de todos os documentos que a política governa |
| Regra de retenção abandonada a continuar a aplicar-se | Retirar uma política é recusado enquanto um tipo de documento a adotar por omissão, e a mensagem nomeia os tipos |
| Quem classifica a decidir quanto tempo se guarda | `admin.documenttypes` gere etiquetas e tipos; alterar políticas de retenção exige `admin.settings` — conformidade e classificação são decisões diferentes |
| Explicação de acesso usada para sondar o alcance alheio | Perguntar pelo acesso de outra pessoa exige `Share` sobre o documento; sobre o próprio basta poder vê-lo. Quem não decide concessões não tem razão para conhecer o alcance dos outros |
| Painel de autorização a divergir da regra que aplica | O painel não calcula: mostra o `EffectiveAccess` produzido pelo mesmo avaliador que decide quando alguém abre, edita ou descarrega |
| Perfil inexistente ou de plataforma nomeado numa concessão | Os perfis oferecidos vêm de `RoleNames`, a lista que a autorização usa, e `SystemAdmin` é excluído: está acima da organização |
| Acesso temporário que nunca acaba | As concessões aceitam prazo, e o predicado de visibilidade respeita-o; um prazo no passado é recusado na validação |
| Métricas a revelarem quem faz quanto | Nenhum instrumento leva organização, utilizador ou documento nas etiquetas: as métricas saem do processo para sistemas com outras regras de acesso, e um contador por organização seria uma fuga lenta de volume de negócio |
| Pilha de chamadas nos registos do worker | A falha de um trabalho é registada com a **mensagem**, nunca com a pilha: quem está de piquete lê estes registos, e uma pilha neles é ruído e uma descrição do interior do processo |
| Um trabalho avariado a parar o resto do trabalho periódico | Cada trabalho corre isolado, com o seu próprio âmbito e captura; o ciclo avança mesmo quando um falha, e ambos os desfechos são contados |
| Sonda de vida usada para reconhecimento | `/health*` devolve nome, estado e duração e nada mais: nunca descrição da falha, exceção, cadeia de ligação ou nome de máquina. Verificado por teste, não assumido |
| Detalhe operacional exposto a quem administra pessoas | O estado do trabalho em segundo plano exige `admin.settings`; `admin.users` não o alcança. Operar a plataforma e gerir contas são trabalhos diferentes |
| Fuga por contagem no painel de operação | As contagens de documentos passam pelo predicado de visibilidade, não apenas pelo filtro por organização: a pergunta é quantos **quem está a ver** vê, não quantos existem |
| Rasto de execução a revelar o interior do processo | A linha de uma corrida falhada guarda a **mensagem**, nunca a pilha, e está limitada a 1024 caracteres — não há onde caber uma |
| Escalada de privilégios | Autorização por permissão no servidor; *claims* do cookie assinadas e revalidadas |
| Injeção SQL | EF Core com consultas parametrizadas; sem concatenação de SQL |
| Adulteração de auditoria | Intercetor de imutabilidade + revogação de permissões na base de dados |
| Divulgação em mensagens de erro | `ProblemDetails` uniformes, sem detalhe interno |

## 12.1 Revisão da superfície de ataque (Fase 9)

A lista acima tem 89 entradas. Uma lista dessas envelhece de uma forma perigosa: cada linha continua
a **parecer** verdade muito depois de o código ter mudado, e uma ameaça listada que já não descreve
o produto é pior do que uma ameaça não listada, porque dá confiança em vez de a pedir.

A revisão foi, por isso, **confirmar afirmações**, não escrever linhas novas. O que se seguiu foi
escolher as afirmações cuja falsidade seria silenciosa e verificá-las contra o código a correr.

### O que passou a ter prova

| Afirmação | Como estava | Como está |
| --- | --- | --- |
| **A sessão vive num cookie `HttpOnly` e em mais lado nenhum** | Regra não-negociável do projeto, **sem um único teste** | `tests/e2e/specs/session-storage.spec.ts` entra na aplicação, enumera tudo o que o browser guardou e recusa qualquer valor com ar de credencial — por nome, por conteúdo e pela forma de um JWT. E confirma que `document.cookie` não vê o cookie de sessão |
| A resposta ao início de sessão não traz credenciais | O teste verificava o cookie, não o corpo | Passa a verificar o corpo. O cookie ser `HttpOnly` não vale nada se a credencial vier no corpo, onde qualquer script a lê |
| As sondas anónimas não descrevem falhas | Assumido | Verificado em `SystemHealthTests`: nome, estado e duração, e nunca exceção, cadeia de ligação ou nome de máquina |

Estas três partilham um modo de falhar, e é o que as torna dignas de teste: **perdem-se por alguém
resolver um problema legítimo**. Uma sessão que não sobrevive a uma janela nova, um pedido que falha
depois de recarregar — guardar o token no `localStorage` resolve os dois, passa a funcionar, e
ninguém repara.

O teste de armazenamento tem um par que é preciso ler em conjunto: o segundo verifica que a sessão
**sobrevive a recarregar a página**. Sem ele, o primeiro seria satisfeito por uma aplicação que não
guarda a sessão em sítio nenhum — e que, por isso, também não a mantém.

### O que já tinha prova, e continua a ter

Imutabilidade da auditoria (a aplicação não altera nem apaga eventos), cabeçalhos de segurança e
identificador de correlação em cada resposta, respostas indistinguíveis entre conta inexistente e
palavra-passe errada, recusa de pedidos que alteram estado sem origem permitida, cobertura dos
filtros por organização, e ausência de `IgnoreQueryFilters` dentro de projeções. As duas últimas
não são testes: falham a **compilação**.

### Duas coisas que estão no repositório de propósito, e que convém dizer

Uma revisão honesta também nomeia o que parece um defeito e não é — senão a próxima pessoa a olhar
gasta uma tarde a chegar à mesma conclusão, ou pior, «corrige-o».

- **A cadeia de ligação de desenvolvimento está versionada**, com a palavra-passe do contentor
  local. É deliberado: o contentor é descartável, publica só em *loopback*, e a cadeia existe para
  que o projeto arranque sem configuração. Em `appsettings.json` — o ficheiro que vale fora de
  desenvolvimento — a cadeia está **vazia**, e o valor real vem do ambiente.
- **A palavra-passe das contas de demonstração está no código do frontend** e é mostrada no ecrã de
  entrada. Também é deliberado: todas as contas são fictícias e o produto é uma demonstração de
  portefólio. Escondê-la seria encenação — quem visita a demonstração precisa de entrar, e fingir
  que aquilo é um segredo não protegeria nada.

### O que a revisão não fez

Não reescreveu a lista, não acrescentou ameaças novas, e não verificou as 89 uma a uma. Verificou as
que **falham em silêncio** e as que a Fase 9 tocou. As restantes descrevem código que não mudou
desde que foram escritas, e cada uma nasceu com o bloco que a tornou verdadeira.

## 13. Trabalho em segundo plano

O worker corre sem organização no contexto ambiente, o que lhe permite processar todas as
organizações. É uma exceção deliberada: o processo não expõe superfície HTTP, os seus trabalhos são
escalonamento de prazos, retoma de temporizadores, fila de IA e varrimento das integrações, e cada
escrita continua a passar pelos mesmos intercetores de auditoria e de isolamento.

O caminho que ignora os filtros de organização está confinado a esses serviços de worker
(`IntegrationRunner`, e não `IntegrationService`, que é o que corre no pedido do utilizador). A
distinção é o que impede que um pedido HTTP alcance, por via de uma integração, dados que a sua
sessão não alcançaria diretamente.

**Um worker de cada vez por trabalho.** Nada impedia que houvesse dois processos, e dois a correr o
mesmo trabalho não dão erro nenhum: dão trabalho repetido, que é pior porque não se vê. A fila de IA
analisaria o mesmo documento duas vezes, as integrações varreriam a mesma caixa de correio em
duplicado, e a verificação de retenção poderia anunciar o mesmo documento a duas velocidades. Cada
trabalho toma um bloqueio antes de correr (`IJobLock`, implementado com `sp_getapplock` do SQL
Server), **sem esperar**: quem não o consegue volta no ciclo seguinte, porque esperar seria juntar os
dois worker no mesmo instante em vez de os manter separados. O bloqueio pertence à sessão, portanto
desaparece sozinho se o processo morrer.

A alternativa habitual seria eleição de líder com um serviço de coordenação à parte. Aqui não faz
falta: a base de dados já é o ponto comum a todos os processos.

## 14. Limitações conhecidas

- Sem autenticação multifator (fora do âmbito desta demonstração; a arquitetura de Identity suporta a
  adição).
- **A CSP protege a API, não a interface.** Os cabeçalhos da secção 6 são emitidos pela API, que
  serve apenas JSON. A interface é servida à parte e, em produção, precisaria da sua própria política
  de segurança de conteúdo no servidor web que a entregasse. Isso não está resolvido, porque a
  história de implantação não está construída.
- Sem rotação automática das chaves de proteção de dados em ambiente distribuído — em produção seria
  necessário um repositório de chaves partilhado.
- A assinatura eletrónica é uma demonstração funcional e **não tem qualquer validade legal**: não há
  chave privada sob controlo do signatário, nem certificado, nem carimbo temporal independente — os
  três elementos que distinguem uma assinatura avançada de um registo. Ver
  [ADR-009](adr/ADR-009-electronic-signature.md), que explica também porque não se gerou um par de
  chaves na aplicação: seria mais convincente e igualmente sem valor.
- O fornecedor de IA de demonstração é determinístico e está identificado como tal: nenhuma resposta
  de demonstração é apresentada como tendo sido gerada por um modelo real.
