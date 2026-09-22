# Guião de demonstração

Todas as organizações, pessoas e documentos são fictícios. Nenhuma parte desta demonstração está
associada a qualquer produto ou fornecedor comercial.

## Preparação (uma vez)

```bash
docker compose up -d                                  # aguarde "healthy"
dotnet run --project src/Api/DocOps.Api.csproj        # http://localhost:5180
npm --prefix src/Web run dev                          # http://localhost:5173
```

Palavra-passe de todas as contas: `Demo!Pass123`

---

## Demonstração disponível na Fase 1 — Identidade, acesso e isolamento (≈4 minutos)

### 1. Página pública (20 s)

Abrir <http://localhost:5173>. Referir a proposta de valor, as seis capacidades e a nota de projeto
independente. Alternar o idioma para inglês e o tema para escuro — a preferência persiste.

### 2. Autenticação real (40 s)

Em **Abrir demonstração interativa**:

- Submeter o formulário vazio: validação no cliente com mensagens em pt-PT.
- Entrar com `ricardo.lopes@asteria.example` e uma palavra-passe errada: resposta `401` com
  mensagem clara, sem revelar se a conta existe.
- Entrar com a palavra-passe correta.

Nas ferramentas de programador, mostrar o cookie `docops.session`: `HttpOnly`, `SameSite=Lax`.
Em `document.cookie` na consola, o cookie **não** aparece — nenhum *token* está acessível a
JavaScript.

### 3. Painel com dados reais (60 s)

O painel mostra o perfil, a organização, os departamentos e as permissões efetivas. Nada disto está
codificado na interface: tudo vem de `/api/auth/me` e `/api/tenants/current`. Na rede, mostrar as
duas respostas.

Reparar que Ricardo Lopes tem `documents.approve` (é aprovador financeiro) mas **não** tem
`admin.users`.

### 4. Isolamento entre organizações (60 s)

Terminar sessão e entrar com `admin@vertice.example`.

A mesma aplicação, o mesmo *endpoint* `/api/tenants/current`, resultado completamente diferente:
Vértice Industrial com dois departamentos. Não existe forma de pedir os dados da Asteria Group — a
organização vem da sessão e não há rota que a aceite como parâmetro.

### 5. Trilho de auditoria (40 s)

Cada tentativa de autenticação ficou registada:

```bash
docker exec docops-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Local_Dev_Passw0rd!' -C -d DocOpsControlTower -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT TOP 10 CONVERT(varchar(19),OccurredAtUtc,120)+' | '+Action+' | '+ActorDisplayName+' | '+ISNULL(Summary,'') FROM AuditEvents ORDER BY OccurredAtUtc DESC;"
```

Mostrar `user.login.failed` (severidade de segurança), `user.login.succeeded` e `user.logout`, cada um
com identificador de correlação. Tentar alterar uma dessas linhas pela aplicação resulta em exceção:
a auditoria é imutável por construção.

### 6. Superfície da API e saúde do sistema (40 s)

- <http://localhost:5180/scalar/v1> — documentação OpenAPI gerada.
- <http://localhost:5180/health> — estado das dependências, sem revelar infraestrutura.
- `curl -X POST http://localhost:5180/api/auth/login -H "Content-Type: application/json" -d '{"email":"a@b.com","password":"x"}'`
  devolve `403 origin_not_allowed`: um pedido sem origem autorizada é recusado, mesmo antes de
  qualquer verificação de credenciais.

---

## Demonstração disponível na Fase 2 — Repositório documental (≈5 minutos)

### 1. Lista de documentos (60 s)

Entre como `ricardo.lopes@asteria.example` e abra **Documentos**. São 41 documentos de demonstração
em 8 tipos, com estados, classificações, versões e etiquetas coerentes.

- Escreva `Northstar` na pesquisa: a lista reduz-se aos documentos desse fornecedor. A pesquisa corre
  no servidor, sobre título, referência, descrição e metadados.
- Clique no estado **Aguarda aprovação**: ficam as faturas e pedidos por decidir.
- Repare no rodapé: a contagem e a paginação vêm do servidor — o browser nunca recebe a lista toda.

### 2. Detalhe, metadados e versões (90 s)

Abra a fatura **Atlas Tecnologia**.

- **Visão geral** mostra as propriedades e os metadados tipados do tipo "Fatura de fornecedor"
  (fornecedor, número, datas, IVA, total, moeda). Os campos não estão codificados na interface: vêm
  do esquema de metadados definido no servidor.
- **Versões** mostra o histórico, com autor, comentário, dimensão e impressão digital SHA-256 de cada
  versão. Descarregue a versão 1 e a versão atual: são ficheiros diferentes, ambos preservados.
- Carregue uma nova versão no painel lateral e veja o número de versão e a impressão digital mudarem.

### 3. Acesso efetivo — a razão por trás da permissão (90 s)

O painel **Acesso efetivo** explica a decisão de autorização, regra a regra:

- "É o proprietário do documento" — concede ver, descarregar, editar, eliminar, partilhar, aprovar.
- "Classificação confidencial: leitura para o departamento responsável".
- "O perfil do utilizador não inclui estas permissões na organização" — remove `eliminar`, porque o
  perfil *Finance Approver* não tem `documents.delete`.

Isto demonstra o princípio: uma partilha de documento nunca concede mais do que o perfil permite.

### 4. Isolamento por utilizador (60 s)

Termine a sessão e entre como `contacto@northstar.example` (parceiro externo, Northstar Supplies).

Abra **Documentos**: vê exatamente **um** documento — a fatura que lhe foi partilhada explicitamente.
Nenhuma classificação abre documentos a um utilizador externo; só uma permissão explícita o faz.

### 5. Trilho de auditoria do documento (60 s)

De volta como Ricardo Lopes, abra um documento e o separador **Auditoria**. A cronologia junta o
histórico do documento (criação, transições de estado, versões) às ações desta sessão (consulta,
descarregamento). Cada entrada tem autor, data e identificador de correlação; as entradas com
alteração mostram o antes e o depois.

---

## Demonstração disponível na Fase 3 — Tarefas e processos (≈5 minutos)

### 1. Centro de tarefas (60 s)

Entre como `ricardo.lopes@asteria.example` e abra **Tarefas**.

- Os indicadores no topo — pendentes, em curso, em atraso, concluídas, por atribuir — são calculados
  pelo servidor a partir das tarefas reais.
- Cada linha mostra o prazo com um ponto de SLA que passa de verde a âmbar e a vermelho à medida que
  a data se aproxima, e uma marca de *escalada* quando o prazo foi ultrapassado.
- Repare numa tarefa **Por atribuir**: foi oferecida ao perfil *FinanceApprover*, não a uma pessoa.
  Está à espera de quem a assumir.
- Os botões disponíveis vêm do servidor: uma tarefa de decisão oferece aprovar e rejeitar; uma tarefa
  de execução oferece concluir.

### 2. Decidir e ver o processo avançar (90 s)

Aprove a tarefa *Aprovar fatura* de `WF-2026-00001` com um comentário.

Abra **Workflows** → a instância `WF-2026-00001`. A cronologia conta a história completa:

```
Processo iniciado
Documento passou de 'Draft' para 'InReview'
Tarefa 'Validar dados da fatura' atribuída a Procurement
Condição 'Valor acima de 10 000 €?' avaliada como verdadeira
Tarefa 'Aprovar fatura' concluída por Ricardo Lopes (Aprovado)
Documento passou de 'Approved' para 'Archived'
Processo concluído
```

O documento ligado está agora **arquivado** — não porque a interface o disse, mas porque o nó final
do processo pediu essa transição e a máquina de estados do documento a aceitou.

### 3. Encaminhamento por condição (60 s)

Abra a definição **Aprovação de fatura de fornecedor** (botão *Passos do processo*). Repare no nó de
condição: faturas acima de 10 000 € seguem para aprovação de direção; abaixo, para aprovação
financeira. As instâncias em curso mostram-no na prática — a fatura da Atlas Tecnologia (30 504 €)
está parada em *Aprovação de direção*, as restantes em *Aprovação financeira*.

### 4. Validação de definições (45 s)

Na mesma página, o painel **Problemas de validação** mostra que o processo é executável. A validação
recusa ativar uma definição com nós sem saída, ramos em falta ou tipos de nó que o motor ainda não
executa — um processo desenhado com nós de IA não pode ser ativado antes de essa fase existir.

### 5. Iniciar um processo a partir de um documento (45 s)

Entre como `ana.ferreira@asteria.example`, abra uma fatura e use o painel **Workflows** na coluna
lateral. Só aparecem processos ativos do tipo desse documento. Inicie um: a tarefa de validação
aparece imediatamente em **Tarefas** para o departamento de Compras, e a notificação no sino do
cabeçalho aparece com ela.

### 6. Trabalho de segundo plano (30 s)

Com o worker a correr (`dotnet run --project src/Worker/DocOps.Worker.csproj`), as tarefas que passam
o prazo são escaladas uma única vez: notificação ao responsável do departamento, entrada na
cronologia da instância e registo de auditoria. Nada disto acontece no pedido de um utilizador.

---

## Demonstração disponível na Fase 4 — Desenhar um processo (≈5 minutos)

Entre como `admin@asteria.example`, que é quem tem a permissão `workflows.design`.

### 1. Abrir o desenhador (30 s)

Em **Workflows**, use **Desenhar** num dos processos de demonstração. O grafo guardado aparece no
diagrama tal como foi desenhado, com o tipo de cada passo, o responsável e o prazo por baixo do nome.
Um aviso no topo diz o que este processo já tem: *«já tem execuções; guardar cria a versão 2»*.

### 2. Criar um processo do zero (90 s)

**Novo workflow** → dê um nome. O processo nasce com início e fim já ligados: nunca abre num estado
que o motor não saiba executar.

Na paleta, adicione uma **Aprovação**. O painel da direita muda para os campos que um passo de
aprovação realmente usa — título da tarefa, a quem é atribuída, prazo, escalonamento, prioridade e o
estado que o documento passa a ter ao entrar no passo. Arraste das pegas dos nós para desenhar as
ligações.

Repare no que a paleta **não** esconde: os nós de IA e de integração aparecem marcados como *ainda
não executáveis*. Podem ser desenhados, mas a validação do servidor impede ativar um processo que os
use.

### 3. Ramos que o motor sabe seguir (45 s)

Selecione a ligação que sai da aprovação. O campo **Ramo** oferece apenas `Aprovado` e `Rejeitado` —
os dois valores que o motor compara quando resolve a saída de um passo com decisão. Numa condição
oferece `Verdadeiro` e `Falso`. Não há ramos livres: um desenho que a interface permite é um desenho
que o motor executa.

### 4. Guardar, validar e ativar (60 s)

**Guardar** grava o grafo; o indicador *Alterações por guardar* desaparece. **Validar** corre a
validação no servidor e lista o que impede a execução. Tente **Ativar** com um passo por configurar:
a resposta é `409` e o processo continua inativo — a decisão é do servidor, não do botão.

### 5. Versionamento com processos a correr (60 s)

Abra um processo que já tenha execuções e altere um passo. O botão diz **Guardar nova versão**. Ao
guardar, o URL muda para o registo da versão 2, o crachá passa a `v2` e a versão anterior fica
marcada como não editável — continua na base de dados porque as instâncias em curso resolvem os nós
nela. Abra uma dessas instâncias em **Workflows → Execuções**: os passos que mostra são os da versão
com que arrancou.

### 6. O que o servidor recusa (45 s)

- **Eliminar** um processo com execuções em curso: `409 workflow_has_running_instances`.
- Guardar a partir de um separador desatualizado: `409 concurrency_conflict`.
- Entrar como `ana.ferreira@asteria.example` (sem `workflows.design`): os botões de desenho não
  aparecem, e um `POST /api/workflows` feito à mão responde `403`.

---

## Demonstração disponível na Fase 5 — Inteligência documental (≈6 minutos)

O ponto desta fase não é que a IA acerte. É que **se possa verificar se acertou**, e que nada chegue
ao documento sem alguém decidir.

### 1. O que chegou e ninguém leu (40 s)

Entre como `ana.ferreira@asteria.example` e abra **Centro de IA**. A primeira coisa visível é o aviso
de que o fornecedor ativo é o **de demonstração**: regras determinísticas sobre o texto, não um
modelo de linguagem. Em **Por classificar** estão os ficheiros recebidos cujo tipo ninguém
identificou ainda — repare que a Ana só vê os dela: o contrato do Jurídico e a fatura britânica das
Finanças são confidenciais e não aparecem aqui.

### 2. Ler o documento (90 s)

Use **Analisar documento** na `fatura-atlas-tecnologia`. Do lado esquerdo fica o **texto real do
documento, numerado**; do lado direito, o que a análise propõe.

A classificação sugere *Fatura de fornecedor* com a confiança e a linha que a suporta. Cada campo
mostra o valor, o grau de confiança e a linha de onde foi lido. **Clique na linha 21** («Total a
pagar: 1 906,50 €»): o texto à esquerda salta para essa linha e realça-a. É isto que distingue uma
citação de uma afirmação — é um sítio onde ir ver.

Repare no que **não** foi encontrado: campos sem valor dizem-no, em vez de adivinhar.

### 3. Nada é aplicado sem decisão (60 s)

Tente **Aplicar ao documento** antes de decidir a classificação: está desativado, e o servidor
recusa com `409 ai_classification_undecided` se for chamado à mão. Mudar o tipo de um documento
nunca é o comportamento por omissão.

Aceite a classificação, use **Aceitar tudo o que foi encontrado**, corrija um valor à mão e rejeite
outro. Só então **Aplicar ao documento**.

### 4. O que ficou registado (60 s)

Em **Visão geral**, o documento passou a *Fatura de fornecedor*, a política de retenção acompanhou o
tipo, e cada valor escrito a partir de uma sugestão aceite tem o selo **IA** com a confiança. O valor
que corrigiu não tem selo — foi escrito por si.

Em **Auditoria** está tudo, decisão a decisão: `ai.run.created`, `ai.classification.accepted`, um
`ai.suggestion.accepted` por campo, `ai.suggestion.edited`, `ai.suggestion.rejected`,
`document.reclassified` e `ai.run.applied`.

### 5. Perguntar ao documento (60 s)

No separador **Inteligência**, use **Qual é o total?**. A resposta vem com a linha que a sustenta, e
clicar nela leva-o lá. Pergunte agora algo que o documento não diz — «Qual é a cor do logótipo?» — e
a resposta é que não foi encontrado, sem citações inventadas.

Entre como `ricardo.lopes@asteria.example`, abra uma fatura já aprovada e pergunte **«Quem aprovou
este documento?»**. A resposta vem dos registos de aprovação do sistema, citando-os — não do texto
do documento, onde essa informação não existe.

### 6. IA dentro de um processo (45 s)

No desenhador de workflows, os nós **Classificação por IA** e **Extração por IA** já são executáveis.
Um passo de IA não chama o fornecedor dentro da travessia: põe a análise em fila, e o worker
(`dotnet run --project src/Worker/DocOps.Worker.csproj`) processa-a e retoma o processo, que escreve
o resultado em variáveis `ai.*` — uma condição a seguir pode encaminhar por elas. O passo **nunca**
aplica nada: as sugestões continuam à espera de uma pessoa.

Um documento sem texto (uma digitalização, por exemplo) faz o processo seguir pelo ramo **Sem
resultado**, em vez de inventar conteúdo: o reconhecimento ótico de caracteres não faz parte desta
demonstração, e o sistema di-lo.

### 7. Com um modelo a sério (30 s, opcional)

`Ai__Provider=OpenAiCompatible` com `Ai__Endpoint`, `Ai__Model` e `Ai__ApiKey` (só do ambiente) troca
o fornecedor. O modelo é tratado com **mais** desconfiança do que as regras: pede-se-lhe as palavras
exatas de onde leu cada valor, e o valor só é sugerido se essas palavras estiverem no documento e o
valor se puder reler a partir delas.

---

## Demonstração disponível na Fase 6 — Pesquisa (≈4 minutos)

Entre como `ana.ferreira@asteria.example` — uma colaboradora sem privilégios especiais, para que se
veja que a pesquisa devolve o que *ela* pode ver.

### 1. Texto livre, com prova de onde veio (1 min)

Abra **Pesquisa** e escreva `atlas tecnologia`.

Repare, por esta ordem:

- Os resultados vêm ordenados por relevância, e o primeiro é aquele que tem os termos no **título**.
  A percentagem ao lado explica-se ao passar o rato: é o peso do sítio onde os termos foram
  encontrados, não um número inventado.
- Por baixo de cada resultado está um **excerto do próprio documento**, com a palavra encontrada
  realçada. A posição do realce vem do servidor; o ecrã não volta a procurar.
- As etiquetas **Encontrado em** dizem em que partes do documento os termos estavam.
- Ao fundo da página, uma linha diz que motor respondeu — «Respondido pelo índice de texto integral
  do SQL Server» — e quais os termos em que a frase foi separada.

Experimente `"prestação de serviços"` entre aspas: a frase é procurada inteira, e a linha de
diagnóstico mostra-a como um único termo.

### 2. Filtros que o resto da aplicação não tem (1 min)

Carregue em **Mostrar filtros**. Além dos habituais, repare em dois:

- **Processo** — de onde está o documento no circuito: sem processo, em curso, concluído, rejeitado.
- **Aprovação** — se aguarda decisão, se já foi aprovado, se foi rejeitado.

Escolha **A aguardar**. Todos os documentos que restam mostram o estado «Aguarda aprovação» — os
dois estados são lidos dos workflows e das tarefas, não de uma cópia guardada no documento.

### 3. Perguntar por palavras (1 min)

Limpe a caixa de pesquisa e escreva na **pergunta em linguagem natural**:

> faturas que aguardam aprovação

Antes dos resultados, a aplicação mostra **como leu a pergunta**: `Aprovação: A aguardar` e
`Tipo: Fatura de fornecedor`. São esses filtros que correram — os mesmos que o formulário produz,
com a mesma autorização. A etiqueta **Demonstração** ao lado diz que quem leu a pergunta foi a
tabela de regras e não um modelo de linguagem.

Experimente agora uma pergunta que ninguém ensinou:

> qualquer coisa que ninguém ensinou

A aplicação diz que **não reconheceu nenhum filtro** e que procurou as palavras como texto livre.
Não inventa uma leitura.

### 4. A pesquisa não vê mais do que quem pesquisa (1 min)

Sem sair, abra uma janela anónima e entre como `admin@asteria.example`. Faça a mesma pesquisa
`atlas tecnologia` nas duas janelas e compare o número de resultados.

O administrador vê mais. Não é a interface a esconder linhas: o predicado de permissões faz parte da
consulta, pelo que os documentos que a Ana não pode ver nunca chegam a ser contados.

## Cenário 1 — Fatura de fornecedor (previsto: Fases 5 a 8)

1. O fornecedor **Northstar Supplies** entra em `/portal` e submete uma fatura.
2. A fatura aparece na caixa de entrada documental da Asteria Group.
3. A IA classifica o documento como *Supplier Invoice*.
4. A IA extrai fornecedor, número, datas, IVA, total, moeda e encomenda associada, com indicadores de
   confiança e campos em falta assinalados.
5. Ana Ferreira valida os campos: aceita em bloco ou campo a campo; cada decisão é auditada.
6. O workflow de aprovação de faturas arranca.
7. Ricardo Lopes (aprovação financeira) recebe a tarefa com prazo e indicador de SLA.
8. Ricardo aprova.
9. O workflow avança: notificação, arquivo.
10. A cronologia mostra cada passo com hora e responsável.
11. Os indicadores do painel refletem a aprovação.
12. O documento fica **Aprovado** e **Arquivado**.

## Cenário 2 — Ciclo de vida de contrato (previsto: Fases 5 a 8)

1. Carregar um contrato.
2. Extrair metadados, incluindo a data de expiração.
3. Iniciar a revisão jurídica (Carla Nunes).
4. Aprovação de direção (Helena Vasconcelos).
5. Pedido de assinatura eletrónica — **demonstração, sem validade legal**.
6. Estado assinado, com *hash* do documento e versão registados.
7. Arquivo.
8. Trilho de auditoria completo, do carregamento ao arquivo.

---

## Pontos a destacar numa entrevista técnica

| Momento | O que demonstra |
| --- | --- |
| Cookie `HttpOnly` e ausência em `document.cookie` | Decisão de segurança consciente sobre armazenamento de sessão |
| Mesma rota, organizações diferentes | Isolamento multi-tenant imposto no servidor |
| `403 origin_not_allowed` | Proteção CSRF sem gestão de *tokens* no cliente |
| Permissões visíveis no painel | Autorização por permissão, guardada em base de dados |
| Auditoria imutável com correlação | Conformidade e diagnóstico ligados pelo mesmo identificador |
| Testes de integração com SQL Server real | Os testes verificam comportamento relacional verdadeiro |
| Painel de acesso efetivo | Autorização por recurso explicável, não apenas um sim/não |
| Impressão digital por versão | Integridade verificável e base para a assinatura eletrónica |
| Filtro aplicado dentro da consulta SQL | A contagem total não revela documentos que o utilizador não pode ver |
| Cronologia do processo | O estado atual é explicável passo a passo, não apenas observável |
| Nó de IA impede a ativação | Falhar explicitamente em vez de saltar um passo do processo |
| Ações da tarefa vindas do servidor | A interface reflete a autorização, não a define |
