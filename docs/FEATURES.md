# O que o DocOps faz, em detalhe

As capacidades agrupadas pelo que são, e não pela ordem em que foram construídas. As decisões com
peso arquitetural, cada uma com as alternativas que ficaram de fora, estão nos [ADR](adr/).

## Fundação, segurança e multi-organização

- Monólito modular .NET 10 com fronteiras explícitas (`Domain`, `Application`, `Infrastructure`, `Api`).
- Base de dados SQL Server modelada com EF Core: chaves estrangeiras, índices, índices únicos
  filtrados, tokens de concorrência, *soft delete* e carimbos temporais automáticos.
- Multi-tenancy com isolamento imposto em duas camadas (filtros globais de leitura + intercetor de
  escrita).
- Autenticação por cookie `HttpOnly` com ASP.NET Core Identity, bloqueio por tentativas e política
  de palavra-passe.
- Autorização por permissões (não por perfis) com permissões guardadas em base de dados e
  políticas geradas a pedido.
- Trilho de auditoria imutável, de autenticações a decisões, com o identificador de correlação do
  pedido em cada registo.
- API REST documentada em OpenAPI, `ProblemDetails` consistente, *rate limiting*, cabeçalhos de
  segurança e proteção CSRF por validação de origem.
- Observabilidade: logging estruturado (Serilog), OpenTelemetry e *health checks* `/health`,
  `/health/live`, `/health/ready`.
- Frontend React 19 + TypeScript com design system próprio, tema claro/escuro, pt-PT/en e
  navegação protegida.

## Repositório documental

- Repositório documental com tipos de documento configuráveis e esquema de metadados por tipo
  (campos tipados, obrigatórios, listas de valores). O formulário da interface é gerado a partir do
  servidor, não codificado no cliente.
- Versionamento real: cada alteração de conteúdo cria uma nova versão com dimensão, `SHA-256` e
  autor; versões anteriores nunca são sobrescritas e continuam descarregáveis.
- Armazenamento de ficheiros atrás de `IFileStorage` (`LocalFileStorage` em disco), com validação de
  extensão, tipo de conteúdo e dimensão, e chave de armazenamento gerada pelo servidor.
- Autorização ao nível do documento: propriedade, permissões explícitas (utilizador, departamento,
  perfil), classificação de sensibilidade e teto imposto pelas permissões do perfil, com um painel
  de **acesso efetivo** que explica cada decisão.
- Máquina de estados do documento (rascunho → revisão → aprovação → aprovado/rejeitado → assinatura →
  arquivo), com arquivo, restauro e eliminação reversível.
- Políticas de retenção por tipo de documento, com data de expiração calculada no arquivo, e uma
  verificação periódica que, quando o prazo termina, **avisa quem administra a organização e não
  elimina nada**: dispor do documento passa a ser permitido, e continua a ser decisão de uma pessoa.
- Filtros da listagem escritos no endereço: uma vista filtrada recarrega-se, guarda-se nos favoritos
  e envia-se a um colega como uma ligação.
- Estados vazios que não inventam a causa: o centro de tarefas nomeia os filtros que estão a
  esconder trabalho e oferece desfazê-los; só afirma que não há trabalho quando não sobra nada
  para alargar.
- Pesquisa e filtros com paginação no servidor, aplicando as regras de acesso dentro da consulta SQL.
- Concorrência otimista ponta a ponta: o cliente devolve o token recebido e uma gravação baseada numa
  leitura desatualizada é recusada.
- Auditoria documental completa: criação, consulta, descarregamento, alteração de metadados, mudança
  de estado, nova versão, concessão e revogação de permissões, arquivo, restauro e eliminação.
- Dados de demonstração: 41 documentos consistentes (com versões, etiquetas, metadados, permissões e
  histórico de auditoria retroativo), 37 em 8 tipos de documento e 4 por classificar.

## Tarefas e motor de workflow

- **Motor de workflow próprio**, orientado a dados: definições com nós e ligações persistidas,
  executadas como instâncias com estado, variáveis e cronologia.
- Tipos de nó executáveis: início, tarefa humana, aprovação, condição, notificação, ramificação
  paralela, junção, temporizador, IA, integração, assinatura e fim. Um passo que o motor não
  soubesse correr **interromperia a instância** em vez de ser ignorado, e um teste de integração
  confirma que cada tipo tem o seu executor.
- **Uma travessia é uma transação**: o motor percorre os nós até cada ramo ficar parado ou terminado
  e só então persiste: instância, tarefas, cronologia e estado do documento ficam sempre coerentes.
- **Centro de tarefas** com âmbitos (minhas / organização / delegadas), indicadores de SLA, prazos,
  prioridade, escalonamento e ações decididas no servidor (aprovar, rejeitar, concluir, pedir
  informação, assumir, reatribuir).
- Tarefas de grupo: uma tarefa oferecida a um departamento ou perfil é visível a quem pertence a esse
  grupo e fica atribuída a quem age sobre ela.
- **Uma execução e as suas tarefas só são visíveis a quem vê o documento**, pelo predicado da
  listagem de documentos, ou a quem participa no processo: quem o iniciou e quem tem ou teve uma
  tarefa nele. Para os restantes respondem 404, e nem `workflows.view` nem `tasks.viewall` chegam.
- **Cronologia por instância**, com transições de estado do documento, decisões, notificações e
  escalonamentos.
- **Validação de definições** (nós sem saída, ramos em falta, nós inalcançáveis, atribuição em falta),
  distinguindo os erros, que impedem a ativação, dos avisos.
- **Worker em processo separado** para o que não pertence ao pedido HTTP: escalonamento de prazos,
  retoma de temporizadores, passos de IA e de integração, integrações agendadas e retenção.
- Notificações na aplicação, com contador por ler no cabeçalho.
- Dados de demonstração: 3 definições e 16 instâncias com tarefas pendentes, em atraso, escaladas,
  concluídas e uma rejeitada, coerentes com o estado dos documentos.

## Desenhador visual de processos

- **Desenhador visual de processos** sobre React Flow: paleta de nós, ligações desenhadas à mão,
  arrasto de posições e painel de propriedades que muda conforme o tipo de nó selecionado.
- Cada campo do painel corresponde a algo que o motor lê. Não há definições decorativas: os ramos
  oferecidos numa ligação são exatamente os que o motor sabe seguir (`true`/`false` numa condição,
  `approved`/`rejected` num passo com decisão).
- **Versionamento por supersessão**: um processo que nunca correu é editado no próprio registo; a
  partir do momento em que tem execuções, guardar cria uma **nova versão** e reforma a anterior, que
  permanece na base de dados porque as instâncias em curso continuam a resolver os nós nela.
- **Concorrência otimista real**: guardar com um token antigo é recusado com `409`, mesmo quando só
  mudou o grafo: o registo da definição é sempre atualizado para que o `rowversion` seja verificado.
- Validação em duas camadas: avisos locais enquanto se desenha (passo sem saída, passo inalcançável,
  ramo em falta) e a validação do servidor, que continua a ser a única que decide se um processo pode
  ser ativado.
- Criar, duplicar (sempre inativo) e eliminar processos; eliminar é recusado enquanto existirem
  execuções em curso.
- O desenhador é carregado à parte (`React.lazy`): quem nunca o abre não descarrega a biblioteca de
  diagramas.

## Inteligência documental

- **Abstração de IA** (`IAiDocumentService`: classificar, extrair, responder) com dois fornecedores:
  um **de demonstração**, determinista e baseado em regras visíveis, e um **opcional compatível com
  OpenAI / Azure OpenAI**, escolhido por configuração e com a chave lida só do ambiente.
- **A origem faz parte do contrato.** Toda a resposta de IA diz se veio do modo de demonstração ou
  de um modelo, e a interface mostra-o onde quer que o resultado apareça. O fornecedor de
  demonstração **não é um modelo de linguagem** e nunca é apresentado como tal.
- **Citações verificadas em código.** O texto do documento é numerado no servidor e cada citação é
  confirmada contra ele. O que não confere é descartado e contado; uma resposta cujas citações caem
  todas não é apresentada. A um modelo exige-se ainda que o valor se possa reler a partir das
  palavras citadas, porque uma citação verdadeira não abona por um valor diferente ao lado.
- **Classificação e extração com revisão obrigatória**: o tipo sugerido e cada campo são aceites,
  corrigidos ou rejeitados por uma pessoa; **cada decisão é auditada** no momento em que é tomada, e
  aplicar escreve pela mesma validação que uma pessoa a editar o formulário atravessa.
- **Tipo «Por classificar»**: a porta de entrada que permite guardar um ficheiro antes de alguém o
  ter lido. Sem ele, o cenário «carregar → classificar → extrair → validar» seria impossível.
- **Perguntar ao documento**, com a linha, a página e a secção de onde vem a resposta; perguntas
  sobre aprovações são respondidas a partir dos registos do sistema, não do texto.
- **Passos de IA executáveis no workflow**: põem a análise em fila e o worker processa-a fora da
  transação do motor, retomando a instância. O passo nunca aplica nada ao documento.
- **Centro de inteligência** em `/app/ai`: documentos por classificar, análises à espera de revisão e
  a taxa de aceitação das sugestões, tudo limitado, no servidor, ao que o utilizador pode ver.

## Pesquisa, caixa de entrada e integrações

**Pesquisa**

- Ecrã de pesquisa em `/app/search` com texto livre, excerto do sítio onde o termo foi encontrado e
  realce da palavra, ordenação por relevância e paginação no servidor.
- Filtros: tipo, departamento, responsável, etiqueta, estado, classificação, intervalo de datas,
  **estado do processo** e **estado de aprovação**, estes dois derivados dos workflows e das
  tarefas de decisão, não guardados no documento.
- Índice de texto integral do SQL Server, criado por migração protegida. Onde o componente não
  existe, a pesquisa compara por subcadeia e **diz na resposta e no ecrã qual dos dois caminhos
  correu** (ADR-005).
- Perguntas em linguagem natural (`POST /api/search/natural-language`): a pergunta é lida para os
  mesmos filtros que o formulário produz, a leitura é mostrada antes dos resultados, e a origem da
  leitura (demonstração ou modelo) é identificada onde quer que apareça.
- A autorização faz parte da consulta: um documento que o utilizador não pode ver não é contado,
  não é ordenado e não é paginado.

**Caixa de entrada documental**

- Caixa de entrada em `/app/inbox`: lista e leitura lado a lado, filtros por estado, por anexo e por
  texto, e contagens do que está por tratar.
- Ações sobre cada mensagem, todas contra a base de dados real: arquivar, reabrir, marcar como
  processada, **guardar um anexo como documento**, **criar documento a partir da mensagem**,
  **associar a um documento existente** e **iniciar um processo** sobre o documento que a mensagem
  produziu.
- Um anexo que se torna documento passa pelo `IDocumentService`, com a mesma validação, referência,
  permissões e auditoria de um carregamento manual. Não há porta lateral para o repositório.
- `IMailConnector` abstrai o sistema de correio com a forma do Microsoft Graph; a implementação de
  demonstração **não precisa de credenciais** e está identificada como simulação onde quer que as
  mensagens apareçam (ADR-007).
- Sincronizar é reconciliar: o identificador da mensagem tem índice único por organização e caixa,
  por isso sincronizar duas vezes não acrescenta nada.

**Centro de integrações**

- Seis cartões em `/app/integrations` (Microsoft Outlook, ERP, CRM, assinatura eletrónica, webhooks
  e importação de ficheiros), cada um com estado, última execução, última atividade, registos
  processados e erros, todos lidos da linha que o servidor escreve depois de uma execução real.
- Cada cartão tem um adaptador por trás, e o trabalho é verdadeiro: o ERP e o CRM reconciliam os
  fornecedores e as contrapartes que estão mesmo nos documentos desta organização contra a lista do
  sistema externo; a importação lê e move ficheiros reais do disco; os webhooks entregam eventos de
  auditoria por HTTPS; a assinatura conta os documentos que aguardam assinatura.
- **Só é dito «simulado» o que é simulado.** O lado de lá do ERP, do CRM e do correio é fixo e
  local, e esses três dizem-no no cartão; os outros três não se rotulam de demonstração por estarem
  atrás de um adaptador (ADR-007).
- Registo por integração, consultável e filtrável por nível, com a divergência concreta e os
  documentos afetados em cada linha.
- Ver e gerir são permissões distintas: `integrations.view` mostra cartões e registos,
  `integrations.manage` é que executa, ativa, desativa e configura.
- O destino dos webhooks é validado **no servidor**, com HTTPS obrigatório e *loopback* e gamas privadas
  recusados, porque um endereço que este servidor vai chamar é uma superfície de SSRF.
- O nó **`Integration`** do workflow é executável: enfileira o pedido, larga a transação
  do motor e o worker retoma a instância quando a execução termina.

## Portal externo e assinatura eletrónica

**Portal externo**

- Experiência separada em `/portal` para parceiros externos, com shell próprio: nenhuma navegação
  interna, nenhum sino de notificações, nada pelo qual um convidado pudesse deduzir que ecrãs a
  organização tem. O cabeçalho identifica a organização que o visitante representa.
- Submeter um documento, acompanhar o estado, descarregar o que enviou e **responder a pedidos de
  informação**.
- **O portal não tem autorização própria.** Quem vê o quê é decidido pelo mesmo avaliador de acesso
  da aplicação interna, para o qual um utilizador externo alcança um documento apenas por
  propriedade ou concessão explícita (ADR-008).
- Os contratos do portal são **tipos próprios, escritos à mão**, e não os internos com menos campos:
  é isso que impede que um campo acrescentado do lado de dentro atravesse sozinho.
- A separação corre **nos dois sentidos**: nenhum perfil interno tem `portal.access`, nem o de
  administrador; um externo que escreva `/app/documents` volta ao portal, e o servidor recusa na
  mesma.
- Uma submissão é criada pelo `IDocumentService`, com a mesma validação, referência, permissões e
  auditoria de um carregamento manual, e entra no departamento que a recebe, com acesso de
  contribuidor, para que alguém lhe possa realmente pegar.
- O fornecedor vê **as perguntas que lhe foram dirigidas e as respostas que deu**, e nada mais da
  cronologia. A lista é construída selecionando dois tipos de evento pelo nome, nunca por exclusão:
  uma lista por exclusão deixaria entrar o tipo de evento que alguém acrescentar amanhã.
- **Responder não faz o processo avançar.** Quem decide é quem perguntou: um fornecedor não aprova
  a sua própria fatura respondendo a uma questão sobre ela. A resposta notifica quem perguntou.

**Assinatura eletrónica**

- **Demonstração de portefólio: não tem validade legal**, e o aviso está no topo do painel, antes de
  tudo o resto. Não é uma assinatura qualificada nem avançada: nada é assinado com chave privada e
  não há autoridade de certificação envolvida.
- O que fica registado é verificável sem confiar na aplicação: o pedido **fixa a versão** e copia o
  **resumo SHA-256** do conteúdo, e o painel mostra-o por extenso.
- Assinar é **recusado se o conteúdo mudou** entretanto: consentir um documento é consentir o seu
  conteúdo. E uma versão nova depois de assinado não apaga nem re-aponta o registo: fá-lo deixar de
  corresponder, e o ecrã di-lo.
- **Só a pessoa nomeada assina.** Nem o administrador da organização, que detém todas as permissões,
  assina em nome de outra. Assinar também **não exige poder editar**: quem é chamado a assinar
  precisa de ver o documento, não de o alterar.
- Um pedido que ninguém poderia cumprir é recusado no momento em que se pede, seja porque o
  signatário não vê o documento, seja porque não tem permissão para assinar.
- Cada passo fica no **histórico do documento**, que é onde se procura: quem pediu, quem assinou ou
  recusou, sobre que versão e com que resumo.
- Nó **`SignatureRequest`** no motor de workflow, que espera por uma pessoa como qualquer tarefa
  humana; um grupo é resolvido para um signatário concreto, porque «o departamento assinou» não é uma
  afirmação sustentável. Está disponível pela API; o desenhador ainda não o oferece na paleta.

Desenho e alternativas rejeitadas: [ADR-009](adr/ADR-009-electronic-signature.md).

## Análises e relatórios

**Análises e relatórios** em `/app/reports`:

- **Indicadores calculados por consulta, nunca guardados**: documentos criados, processos iniciados
  e concluídos, tarefas em atraso, tempo médio de processamento, taxa de automação, tempo até
  aprovação.
- **Cada painel é a fatia de quem o abre.** As consultas passam pelo mesmo predicado de visibilidade
  da listagem, da pesquisa, do portal e das execuções de processos, porque um total que incluísse
  documentos invisíveis revelaria por agregação aquilo que a lista esconde.
- **Uma medida sem base diz que não tem base**, em vez de dizer zero. Zero afirma que se mediu; num
  período em que nada aconteceu é uma mentira com aspeto de facto.
- Tendências por dia, distribuições por estado, tipo e departamento, e **onde os processos param**:
  os passos com trabalho à espera e há quanto tempo.
- Gráficos em SVG construídos à mão, um por medida. Medidas de escalas diferentes não partilham
  eixo. Exportação para CSV atrás da permissão `reports.export`.

**Administração** em `/app/admin`:

- Criar contas com **palavra-passe inicial mostrada uma única vez**. O servidor guarda o resumo
  criptográfico e não a sabe devolver. Atribuir perfis e departamentos, desativar e reativar.
- **A própria conta não se edita a partir daqui.** Tirar a si mesmo a administração, ou desativar-se,
  pode deixar a organização sem ninguém capaz de desfazer.
- **Contas são desativadas, nunca eliminadas**: a auditoria e os documentos referem-se a elas.
- `SystemAdmin` não é atribuível de dentro de uma organização, e a conta que administra a plataforma
  não se gere a partir de uma organização. A recusa é do servidor, e não só da interface.
- Departamentos com código único por organização, criação e edição.

**Catálogo de documentos** no separador Catálogo de `/app/admin`:

- **Tipos de documento** com código, prefixo de referência, confidencialidade por omissão e política
  de retenção. Dois tipos nunca partilham prefixo: a referência é o que as pessoas citam umas às
  outras, e duas iguais para documentos diferentes tornam-na inútil.
- **Campos de metadados por tipo**: texto, número, data, sim/não, montante e escolha com opções.
- **As recusas são a funcionalidade.** Um campo que documentos já preenchem **não muda de tipo** (o
  valor está na coluna do tipo atual e deixaria de ser legível) e **não se elimina** (apagaria o que
  esses documentos dizem). Tornar um campo obrigatório é recusado se algum documento ficasse por
  preencher: é uma afirmação sobre o que já existe, e seria falsa no momento em que fosse guardada.
- **O tipo de entrada não se desativa nem aceita campos obrigatórios.** É por ele que entra um
  ficheiro antes de alguém o ter lido, incluindo os que chegam por correio e pelo portal.
- Cada recusa traz a contagem que a explica, e aparece **dentro do formulário** que a provocou.

**Etiquetas e retenção** nos separadores Etiquetas e Retenção de `/app/admin`:

- **Remover uma etiqueta é permitido e reversível**, e a distinção é deliberada: um valor de
  metadados é o que o documento **afirma**, e por isso não se apaga; uma etiqueta é uma anotação
  **colocada sobre** ele. As ligações aos documentos sobrevivem à remoção, e restaurar traz-lhes o
  número de volta ao que era.
- Nome único entre as que estão em circulação: um nome libertado por uma remoção volta a estar
  livre. Cor validada como hexadecimal e **recusada**, não ignorada: uma cor que não chega é uma
  cor que alguém escolheu e nunca viu aplicada.
- **Mudar a duração de uma política de retenção é permitido**, ao contrário do que acontece no
  catálogo. As obrigações mudam de facto, com a lei e com os contratos, e um catálogo incapaz de as
  acompanhar é que seria a mentira. A mudança fica na auditoria com o valor antigo e o novo, porque
  mexe na data de eliminação de todos os documentos que a política governa.
- **Retirar uma política que tipos de documento adotam é recusado**, e a mensagem nomeia-os: esses
  tipos continuariam a aplicar uma regra que a organização decidiu deixar de aplicar.
- Gerir etiquetas é `admin.documenttypes`; decidir **quanto tempo se guardam documentos** é
  `admin.settings`, porque é uma decisão de conformidade, não de classificação.

**Editor de permissões** no separador Permissões de cada documento:

- **Concessões por pessoa, departamento e perfil.** Os perfis vêm do servidor
  (`GET /api/tenants/current/roles`) e não de uma lista escrita no ecrã: duas listas de perfis
  divergem, e a que a interface mostra é a que ninguém repara que está errada. A administração da
  plataforma não é oferecida, porque está acima da organização.
- **Concessões com prazo**, para a janela de um auditor externo. Um prazo já passado é recusado: uma
  janela de acesso que fechou antes de abrir não é uma janela.
- **«Porque é que alguém alcança este documento».** Mostrar só o acesso
  de quem está a olhar responde a metade da pergunta; quem concede precisa de saber porque é que
  **outra** pessoa alcança, ou não alcança. O painel mostra a mesma avaliação que o servidor usa ao
  decidir, regra a regra: o que cada uma concede e o que cada restrição retira.
- O servidor só responde sobre outra pessoa a quem possa partilhar o documento. Perguntar «esta
  pessoa conseguiria fazer isto?» é legítimo para quem decide conceder, e mais ninguém.

**Saúde do sistema** no separador Sistema de `/app/admin`:

- **O que cada trabalho em segundo plano andou a fazer**: última corrida, desfecho, corridas e
  falhas nas últimas 24 horas, duração média. Os quatro aparecem sempre, mesmo os que nunca
  correram: um trabalho ausente do ecrã e um trabalho que nunca correu parecem o mesmo a quem lê, e
  não são a mesma coisa.
- **Trabalho à espera** nesta organização: tarefas em atraso, integrações em fila, análises de IA
  por correr, documentos a aguardar aprovação e assinaturas pendentes. Números, nunca
  identificadores.
- **As sondas anónimas continuam tersas.** `/health`, `/health/live` e `/health/ready` devolvem
  nome, estado e duração, e nunca uma mensagem de exceção, uma cadeia de ligação ou um nome de
  máquina, que é o que transforma uma sonda de vida em reconhecimento. O detalhe fica atrás de
  `admin.settings`.

## Melhorias futuras

- Pôr o nó de assinatura na paleta do desenhador, com a escolha do signatário no painel de
  propriedades.
- A mesma verificação do valor para todos os fornecedores, os números de linha ligados à extração
  guardada com cada versão, e um estado «não verificada» visível em vez de uma citação descartada.
- Endereço ou campo de organização no início de sessão, criação de organizações pela aplicação, e
  adesão de uma pessoa a várias, com seletor de contexto auditado.
- OIDC com o padrão BFF, mantendo a sessão em cookie `HttpOnly`, e autenticação multifator.
- Adaptador de armazenamento de objetos, compatível com S3.
- Paginação por chave e contagens com teto, para catálogos de milhões de documentos.
- Auditoria sem dados pessoais nos registos, e exportação assinada criptograficamente.
- Conetores reais por trás dos adaptadores que hoje simulam o lado de lá (Microsoft Graph para o
  correio, APIs de ERP e CRM). A interface não muda, só a implementação registada.
- Extração do módulo de IA para processo próprio, caso o volume o justifique.
- Aplicação instalável (PWA) com trabalho offline para consulta documental.
- Idiomas adicionais (es, fr, ro), que a arquitetura de localização já suporta.
