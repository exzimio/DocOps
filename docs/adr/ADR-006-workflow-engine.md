# ADR-006 — Motor de workflow próprio, orientado a dados

- **Estado:** aceite — motor implementado na Fase 3; designer visual na Fase 4
- **Data:** 2026-09-18
- **Âmbito:** definição, execução, prazos e cronologia de processos

## Contexto

O produto precisa de processos configuráveis pelo utilizador (aprovação de faturas, revisão de
contratos) com tarefas humanas, condições, ramificações paralelas, temporizadores, escalonamento,
reatribuição e uma cronologia auditável de tudo o que aconteceu. As definições são desenhadas
visualmente e têm de ser persistidas, versionadas e ativáveis.

## Decisão

Implementar um motor próprio, pequeno e orientado a dados:

- **Definição** — `WorkflowDefinition` com `WorkflowNode` e `WorkflowEdge`; cada nó tem um tipo
  (início, tarefa humana, aprovação, condição, notificação, classificação por IA, extração por IA,
  integração, ramo paralelo, temporizador, fim) e propriedades em JSON validadas por tipo de nó.
- **Execução** — `WorkflowInstance` guarda o estado atual, as variáveis do processo e os nós ativos.
  O avanço é sempre uma transição explícita, persistida numa transação com o efeito que a originou
  (concluir tarefa, alterar estado do documento, escrever auditoria).
- **Tarefas** — `WorkflowTask` representa trabalho humano com responsável, prazo, prioridade,
  estado e histórico de reatribuições.
- **Tempo** — temporizadores, deteção de atrasos e escalonamento correm no *worker* em segundo plano;
  nenhum pedido HTTP espera por trabalho de longa duração.
- **Cronologia** — cada transição escreve um evento com hora, ator, nó de origem, nó de destino e
  decisão, o que produz a linha temporal apresentada na interface.

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
definição ativa não afeta instâncias já em execução — cada instância guarda a versão da definição com
que arrancou.

## Como ficou, na prática

- **Uma travessia é uma transação.** O motor percorre os nós até cada ramo ficar parado ou terminado
  e só então grava. Instância, tarefas, cronologia e estado do documento ficam sempre coerentes.
- **Falhar em vez de saltar.** Um tipo de nó sem executor, uma ligação em falta ou uma transição de
  documento ilegal param a instância e registam o motivo na cronologia. Foi um teste de integração
  que impôs esta escolha: a primeira versão tentava arquivar um documento em rascunho e teria ficado
  num estado inconsistente se o motor fosse permissivo.
- **Condições sem linguagem de expressões.** Um operador de um conjunto fechado sobre uma variável
  nomeada chega para as regras que um utilizador de negócio desenha, e não abre caminho a executar
  código a partir de dados guardados.
- **O motor não contorna o documento.** As alterações de estado configuradas num nó passam por
  `DocumentLifecycle`, tal como as que um utilizador faz pela interface.
- **Junções contam chegadas em variáveis da instância**, pelo que o estado de uma ramificação
  paralela sobrevive a um reinício como o resto da instância.
