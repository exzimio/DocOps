# ADR-006: Motor de workflow próprio, orientado a dados

- **Estado:** aceite
- **Data:** 2026-09-18
- **Âmbito:** definição, execução, prazos e cronologia de processos

## Contexto

O produto precisa de processos configuráveis pelo utilizador (aprovação de faturas, revisão de
contratos) com tarefas humanas, condições, ramificações paralelas, temporizadores, escalonamento,
reatribuição e uma cronologia auditável de tudo o que aconteceu. As definições são desenhadas
visualmente e têm de ser persistidas, versionadas e ativáveis.

## Decisão

Implementar um motor próprio, pequeno e orientado a dados:

- **Definição**: `WorkflowDefinition` com `WorkflowNode` e `WorkflowEdge`; cada nó tem um tipo
  (início, tarefa humana, aprovação, condição, notificação, classificação por IA, extração por IA,
  integração, ramo paralelo, temporizador, fim) e propriedades em JSON validadas por tipo de nó.
- **Execução**: `WorkflowInstance` guarda o estado atual, as variáveis do processo e os nós ativos.
  O avanço é sempre uma transição explícita, gravada numa só transação com o que a originou e o que
  provocou: a tarefa concluída, as tarefas novas, a cronologia, o estado do documento e as
  notificações. A auditoria é escrita depois de essa gravação ter sucesso, no seu próprio contexto,
  para registar só o que de facto aconteceu.
- **Tarefas**: `WorkflowTask` representa trabalho humano com responsável, prazo, prioridade,
  estado e histórico de reatribuições.
- **Tempo**: temporizadores, deteção de atrasos e escalonamento correm no *worker* em segundo plano;
  nenhum pedido HTTP espera por trabalho de longa duração.
- **Cronologia**: cada transição escreve um evento com hora, ator, o nó em que aconteceu e, quando
  há, a decisão e o comentário, o que produz a linha temporal apresentada na interface.

## Alternativas consideradas

**Elsa Workflows.** Motor .NET maduro e capaz. Rejeitado por duas razões: traz o seu próprio modelo
de persistência e de identidade, o que colidiria com o isolamento por organização e com o modelo de
auditoria desta aplicação; e, num portefólio cujo objetivo é demonstrar desenho de sistemas, delegar
a peça mais interessante esconderia exatamente aquilo que se pretende mostrar.

**Windows Workflow Foundation / motores BPMN completos.** Rejeitado. BPMN 2.0 traz uma superfície
enorme (eventos de compensação, subprocessos, correlação de mensagens) da qual o produto usaria uma
fração mínima, ao custo de complexidade em todo o lado.

**Máquina de estados fixa em código.** Rejeitado. Os processos são configuráveis pelo cliente; uma
máquina de estados compilada obrigaria a um *deploy* por cada alteração de circuito.

**Event sourcing para o estado da instância.** Rejeitado. A cronologia já dá o histórico completo e
auditável sem impor reconstrução de estado por reprodução de eventos, projeções e versionamento de
eventos.

## Consequências

**Positivas**
- O motor conhece o domínio: tarefas, prazos e decisões são entidades de primeira classe, não
  variáveis genéricas.
- Uma transição é uma transação: não existe estado intermédio visível nem necessidade de compensação.
- A cronologia é um produto natural da execução, não um mecanismo à parte.

**Negativas**
- Menos funcionalidades do que um motor BPMN (sem compensação, sem correlação de mensagens, sem
  subprocessos reutilizáveis).
- Manutenção do motor é responsabilidade do projeto.

**Limites aceites e explícitos:** o motor executa grafos dirigidos com ramificação condicional e
paralela e junção; não implementa a especificação BPMN nem transações de compensação. Alterar uma
definição ativa não afeta instâncias já em execução, porque cada instância guarda a versão da definição com
que arrancou.

## Como ficou, na prática

- **Uma travessia é uma transação.** O motor percorre os nós até cada ramo ficar parado ou terminado
  e só então grava, num único `SaveChanges`: instância, tarefas, cronologia, estado do documento e
  notificações das tarefas novas. A auditoria dessas mudanças é escrita depois de a gravação ter
  sucesso, para nunca descrever uma mudança desfeita.
- **Falhar em vez de saltar.** Um tipo de nó sem executor, uma ligação em falta ou uma transição de
  documento ilegal param a instância e registam o motivo na cronologia. Foi um teste de integração
  que impôs esta escolha: a primeira versão tentava arquivar um documento em rascunho e teria ficado
  num estado inconsistente se o motor fosse permissivo.
- **Condições sem linguagem de expressões.** Um operador de um conjunto fechado sobre uma variável
  nomeada chega para as regras que um utilizador de negócio desenha, e não abre caminho a executar
  código a partir de dados guardados.
- **O motor não contorna o documento.** As alterações de estado configuradas num nó passam por
  `DocumentLifecycle`, tal como as que um utilizador faz pela interface.
- **Uma junção espera pelos ramos que ainda a podem alcançar, e não por um número de chegadas.**
  Quando um ramo lá chega, a junção pergunta se algum outro nó ativo da instância tem caminho até
  ela; se nenhum tem, deixa passar um só ramo. A primeira versão contava as ligações de entrada, e
  falhava de três maneiras: esperava para sempre quando uma condição desviava um ramo, esperava para
  sempre quando as duas saídas de uma condição entravam na mesma junção, e contava uma chegada a
  mais quando um temporizador voltava a percorrer a instância com a junção em espera. A resposta
  nova não guarda estado próprio: os nós ativos já são gravados com a instância. No fim de cada
  travessia, uma junção em espera que já não pode receber nenhum ramo é libertada.
- **Terminar é terminar tudo.** Um nó de fim termina o processo inteiro, e uma falha também. As
  tarefas ainda abertas noutros ramos são canceladas nesse momento, porque já não poderiam ser
  decididas, e uma tarefa criada e cancelada na mesma travessia não gera notificação.
- **Cada temporizador guarda o seu próprio momento**, numa variável da instância com o nome do nó. A
  data de retoma da instância, que o worker procura, é calculada na gravação como o mais próximo
  desses momentos. Com uma só data por instância, dois temporizadores em ramos paralelos
  partilhavam-na: o primeiro a terminar apagava-a, e o segundo recomeçava do zero.
- **Alterar um processo em uso cria uma versão nova.** A definição antiga não é arquivada nem
  apagada: fica no seu próprio registo porque as instâncias em curso continuam a resolver os nós
  nela. O índice único é `(TenantId, Code, Version)` precisamente para que isso seja possível.
- **O desenhador não inventa vocabulário.** Os ramos que oferece numa ligação são os que
  `ResolveNextNodes` sabe comparar, e os campos que mostra num nó são os que o executor desse tipo
  lê. Um desenho que a interface permite é um desenho que o motor executa.
