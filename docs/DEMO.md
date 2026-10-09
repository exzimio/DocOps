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

### O catálogo de demonstração

A base semeada tem duas organizações sem nada em comum, a Asteria Group e a Vértice Industrial. O
catálogo da Asteria tem 41 documentos, quatro dos quais são documentos de entrada **«Por
classificar»**, com texto realista, para a demonstração da inteligência documental. Analisá-los e
aplicar as sugestões reclassifica-os, e deixam de estar por classificar. As integrações começam
**«Por configurar»**, com tudo a zero, porque nenhuma correu ainda.

Tudo o que se faz numa demonstração é real, e por isso muda o catálogo. Para o repor no estado
semeado antes de mostrar o produto a alguém:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/reset-demo-db.ps1
```

O guião para a API, larga a base de dados, recria as três identidades de base de dados e apaga os
ficheiros carregados em `src/Api/storage`. A API volta a semear tudo no arranque seguinte.

---

## Identidade, acesso e isolamento (≈4 minutos)

### 1. Página pública (20 s)

Abrir <http://localhost:5173>. Referir a proposta de valor, as seis capacidades e a nota de projeto
independente. Alternar o idioma para inglês e o tema para escuro: a preferência persiste.

### 2. Autenticação real (40 s)

Em **Abrir demonstração interativa**:

- Submeter o formulário vazio: validação no cliente com mensagens em pt-PT.
- Entrar com `ricardo.lopes@asteria.example` e uma palavra-passe errada: resposta `401` com
  mensagem clara, sem revelar se a conta existe.
- Entrar com a palavra-passe correta.

Nas ferramentas de programador, mostrar o cookie `docops.session`: `HttpOnly`, `SameSite=Lax`.
Em `document.cookie` na consola, o cookie **não** aparece: nenhum *token* está acessível a
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
Vértice Industrial com dois departamentos. Não existe forma de pedir os dados da Asteria Group: a
organização vem da sessão e não há rota que a aceite como parâmetro.

### 5. Trilho de auditoria (40 s)

Cada tentativa de autenticação ficou registada. Para as ler de fora da aplicação é preciso
declarar o modo de sistema **e** ligar-se como `app_system`: a política de segurança ao nível da
linha nega por omissão.

```bash
docker cp scripts/demo docops-sqlserver:/tmp/
docker exec --env-file .env docops-sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U app_system -P $DOCOPS_SYSTEM_PASSWORD -C -d DocOpsControlTower -h -1 -W -i /tmp/demo/audit-as-system.sql'
```

Mostrar `user.login.failed` (severidade de segurança), `user.login.succeeded` e `user.logout`.

**E a mesma consulta como `sa` devolve zero linhas**, o que vale a pena mostrar porque é o ponto:

```bash
docker exec --env-file .env docops-sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $MSSQL_SA_PASSWORD -C -d DocOpsControlTower -h -1 -W -i /tmp/demo/audit-as-administrator.sql'   # 0
```

A segurança ao nível da linha não isenta administradores, e `sa` mapeia para `dbo`: a dispensa exige
`USER_NAME() = 'app_system'`, e saber o nome da chave não chega. Ver
[`SECURITY.md`](SECURITY.md), secção 4.1.

Alterar uma dessas linhas pela aplicação resulta em exceção, e **em SQL cru resulta em recusa do
motor**: as identidades da aplicação têm `UPDATE` e `DELETE` negados nesta tabela. A auditoria é
imutável nas duas camadas.

### 6. Superfície da API e saúde do sistema (40 s)

- <http://localhost:5180/scalar/v1>: documentação OpenAPI gerada.
- <http://localhost:5180/health>: estado das dependências, sem revelar infraestrutura.
- `curl -X POST http://localhost:5180/api/auth/login -H "Content-Type: application/json" -d '{"email":"a@b.com","password":"x"}'`
  devolve `403 origin_not_allowed`: um pedido sem origem autorizada é recusado, mesmo antes de
  qualquer verificação de credenciais.

---

## Repositório documental (≈5 minutos)

### 1. Lista de documentos (60 s)

Entre como `ricardo.lopes@asteria.example` e abra **Documentos**. São 41 documentos de demonstração
em 8 tipos, com estados, classificações, versões e etiquetas coerentes.

- Escreva `Northstar` na pesquisa: a lista reduz-se aos documentos desse fornecedor. A pesquisa corre
  no servidor, sobre título, referência, descrição e metadados.
- Clique no estado **Aguarda aprovação**: ficam as faturas e pedidos por decidir.
- Repare no rodapé: a contagem e a paginação vêm do servidor: o browser nunca recebe a lista toda.

### 2. Detalhe, metadados e versões (90 s)

Abra a fatura **Atlas Tecnologia**.

- **Visão geral** mostra as propriedades e os metadados tipados do tipo "Fatura de fornecedor"
  (fornecedor, número, datas, IVA, total, moeda). Os campos não estão codificados na interface: vêm
  do esquema de metadados definido no servidor.
- **Versões** mostra o histórico, com autor, comentário, dimensão e impressão digital SHA-256 de cada
  versão. Descarregue a versão 1 e a versão atual: são ficheiros diferentes, ambos preservados.
- Carregue uma nova versão no painel lateral e veja o número de versão e a impressão digital mudarem.

### 3. Acesso efetivo: a razão por trás da permissão (90 s)

O painel **Acesso efetivo** explica a decisão de autorização, regra a regra:

- "É o proprietário do documento": concede ver, descarregar, editar, eliminar, partilhar, aprovar.
- "Classificação confidencial: leitura para o departamento responsável".
- "O perfil do utilizador não inclui estas permissões na organização": remove `eliminar`, porque o
  perfil *Finance Approver* não tem `documents.delete`.

Isto demonstra o princípio: uma partilha de documento nunca concede mais do que o perfil permite.

### 4. Isolamento por utilizador (60 s)

Termine a sessão e entre como `contacto@northstar.example` (parceiro externo, Northstar Supplies).

Abra **Documentos**: vê exatamente **um** documento, a fatura que lhe foi partilhada explicitamente.
Nenhuma classificação abre documentos a um utilizador externo; só uma permissão explícita o faz.

### 5. Trilho de auditoria do documento (60 s)

De volta como Ricardo Lopes, abra um documento e o separador **Auditoria**. A cronologia junta o
histórico do documento (criação, transições de estado, versões) às ações desta sessão (consulta,
descarregamento). Cada entrada tem autor, data e identificador de correlação; as entradas com
alteração mostram o antes e o depois.

---

## Tarefas e processos (≈5 minutos)

### 1. Centro de tarefas (60 s)

Entre como `ricardo.lopes@asteria.example` e abra **Tarefas**.

- Os indicadores no topo (pendentes, em curso, em atraso, concluídas, por atribuir) são calculados
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

O documento ligado está agora **arquivado**, não porque a interface o disse, mas porque o nó final
do processo pediu essa transição e a máquina de estados do documento a aceitou.

### 3. Encaminhamento por condição (60 s)

Abra a definição **Aprovação de fatura de fornecedor** (botão *Passos do processo*). Repare no nó de
condição: faturas acima de 10 000 € seguem para aprovação de direção; abaixo, para aprovação
financeira. As instâncias em curso mostram-no na prática: a fatura da Atlas Tecnologia (30 504 €)
está parada em *Aprovação de direção*, as restantes em *Aprovação financeira*.

### 4. Validação de definições (45 s)

Na mesma página, o painel **Problemas de validação** mostra que o processo é executável. A validação
recusa ativar uma definição com nós sem saída, ramos em falta ou tipos de nó que o motor não
executa.

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

## Desenhar um processo (≈5 minutos)

Entre como `admin@asteria.example`, que é quem tem a permissão `workflows.design`.

### 1. Abrir o desenhador (30 s)

Em **Workflows**, use **Desenhar** num dos processos de demonstração. O grafo guardado aparece no
diagrama tal como foi desenhado, com o tipo de cada passo, o responsável e o prazo por baixo do nome.
Um aviso no topo diz o que este processo já tem: *«já tem execuções; guardar cria a versão 2»*.

### 2. Criar um processo do zero (90 s)

**Novo workflow** → dê um nome. O processo nasce com início e fim já ligados: nunca abre num estado
que o motor não saiba executar.

Na paleta, adicione uma **Aprovação**. O painel da direita muda para os campos que um passo de
aprovação realmente usa: título da tarefa, a quem é atribuída, prazo, escalonamento, prioridade e o
estado que o documento passa a ter ao entrar no passo. Arraste das pegas dos nós para desenhar as
ligações.

Repare que a paleta oferece também passos de IA e de integração, e que nenhum vem marcado como
experimental: o desenhador só oferece o que o motor sabe correr.

### 3. Ramos que o motor sabe seguir (45 s)

Selecione a ligação que sai da aprovação. O campo **Ramo** oferece apenas `Aprovado` e `Rejeitado`,
os dois valores que o motor compara quando resolve a saída de um passo com decisão. Numa condição
oferece `Verdadeiro` e `Falso`. Não há ramos livres: um desenho que a interface permite é um desenho
que o motor executa.

### 4. Guardar, validar e ativar (60 s)

**Guardar** grava o grafo; o indicador *Alterações por guardar* desaparece. **Validar** corre a
validação no servidor e lista o que impede a execução. Tente **Ativar** com um passo por configurar:
a resposta é `409` e o processo continua inativo: a decisão é do servidor, não do botão.

### 5. Versionamento com processos a correr (60 s)

Abra um processo que já tenha execuções e altere um passo. O botão diz **Guardar nova versão**. Ao
guardar, o URL muda para o registo da versão 2, o crachá passa a `v2` e a versão anterior fica
marcada como não editável, e continua na base de dados porque as instâncias em curso resolvem os nós
nela. Abra uma dessas instâncias em **Workflows → Execuções**: os passos que mostra são os da versão
com que arrancou.

### 6. O que o servidor recusa (45 s)

- **Eliminar** um processo com execuções em curso: `409 workflow_has_running_instances`.
- Guardar a partir de um separador desatualizado: `409 concurrency_conflict`.
- Entrar como `ana.ferreira@asteria.example` (sem `workflows.design`): os botões de desenho não
  aparecem, e um `POST /api/workflows` feito à mão responde `403`.

---

## Inteligência documental (≈6 minutos)

O ponto desta demonstração não é que a IA acerte. É que **se possa verificar se acertou**, e que nada chegue
ao documento sem alguém decidir.

### 1. O que chegou e ninguém leu (40 s)

Entre como `ana.ferreira@asteria.example` e abra **Centro de IA**. A primeira coisa visível é o aviso
de que o fornecedor ativo é o **de demonstração**: regras determinísticas sobre o texto, não um
modelo de linguagem. Em **Por classificar** estão os ficheiros recebidos cujo tipo ninguém
identificou ainda. Repare que a Ana só vê os dela: o contrato do Jurídico e a fatura britânica das
Finanças são confidenciais e não aparecem aqui.

### 2. Ler o documento (90 s)

Use **Analisar documento** na `fatura-atlas-tecnologia`. Do lado esquerdo fica o **texto real do
documento, numerado**; do lado direito, o que a análise propõe.

A classificação sugere *Fatura de fornecedor* com a confiança e a linha que a suporta. Cada campo
mostra o valor, o grau de confiança e a linha de onde foi lido. **Clique na linha 21** («Total a
pagar: 1 906,50 €»): o texto à esquerda salta para essa linha e realça-a. É isto que distingue uma
citação de uma afirmação: é um sítio onde ir ver.

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
que corrigiu não tem selo: foi escrito por si.

Em **Auditoria** está tudo, decisão a decisão: `ai.run.created`, `ai.classification.accepted`, um
`ai.suggestion.accepted` por campo, `ai.suggestion.edited`, `ai.suggestion.rejected`,
`document.reclassified` e `ai.run.applied`.

### 5. Perguntar ao documento (60 s)

No separador **Inteligência**, use **Qual é o total?**. A resposta vem com a linha que a sustenta, e
clicar nela leva-o lá. Pergunte agora algo que o documento não diz, como «Qual é a cor do logótipo?», e
a resposta é que não foi encontrado, sem citações inventadas.

Entre como `ricardo.lopes@asteria.example`, abra uma fatura já aprovada e pergunte **«Quem aprovou
este documento?»**. A resposta vem dos registos de aprovação do sistema, citando-os, e não do texto
do documento, onde essa informação não existe.

### 6. IA dentro de um processo (45 s)

No desenhador de workflows, os nós **Classificação por IA** e **Extração por IA** já são executáveis.
Um passo de IA não chama o fornecedor dentro da travessia: põe a análise em fila, e o worker
(`dotnet run --project src/Worker/DocOps.Worker.csproj`) processa-a e retoma o processo, que escreve
o resultado em variáveis `ai.*`, e uma condição a seguir pode encaminhar por elas. O passo **nunca**
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

## Pesquisa (≈4 minutos)

Entre como `ana.ferreira@asteria.example`, uma colaboradora sem privilégios especiais, para que se
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
- Ao fundo da página, uma linha diz que motor respondeu («Respondido pelo índice de texto integral
  do SQL Server») e quais os termos em que a frase foi separada.

Experimente `"prestação de serviços"` entre aspas: a frase é procurada inteira, e a linha de
diagnóstico mostra-a como um único termo.

### 2. Filtros que o resto da aplicação não tem (1 min)

Carregue em **Mostrar filtros**. Além dos habituais, repare em dois:

- **Processo**: em que ponto do circuito está o documento (sem processo, em curso, concluído, rejeitado).
- **Aprovação**: se aguarda decisão, se já foi aprovado, se foi rejeitado.

Escolha **A aguardar**. Todos os documentos que restam mostram o estado «Aguarda aprovação», e os
dois estados são lidos dos workflows e das tarefas, não de uma cópia guardada no documento.

### 3. Perguntar por palavras (1 min)

Limpe a caixa de pesquisa e escreva na **pergunta em linguagem natural**:

> faturas que aguardam aprovação

Antes dos resultados, a aplicação mostra **como leu a pergunta**: `Aprovação: A aguardar` e
`Tipo: Fatura de fornecedor`. Foram esses filtros que correram, os mesmos que o formulário produz e
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

## Caixa de entrada (≈4 minutos)

Entre como `sofia.martins@asteria.example`, que tem `inbox.process` e pode agir sobre as mensagens.

### 1. O que chegou, e de onde (1 min)

Abra **Caixa de entrada**. Repare, antes de tudo, na faixa âmbar no topo: diz que esta é uma **caixa
de correio de demonstração**, gerada localmente, sem ligação a servidor nenhum e sem credenciais.
Não é letra pequena: é a mesma regra da IA, aplicada ao correio. Uma simulação diz que é
uma simulação, onde quer que apareça.

A lista tem correspondência realista: uma fatura da Atlas Tecnologia com anexo, uma minuta de
contrato do jurídico da Nordhavn, uma cobrança em atraso marcada como **Alta**, um catálogo de
papelaria já arquivado. À direita, a mensagem selecionada abre-se para leitura.

Carregue em **Sincronizar**. A linha por baixo diz quantas mensagens foram obtidas e quantas eram
novas: **zero**. Sincronizar de novo não acrescenta nada, porque o identificador que o sistema de
correio dá a cada mensagem tem índice único por organização e caixa. O botão vai ser carregado duas
vezes por alguém, e a segunda vez não pode duplicar a caixa de entrada.

### 2. Uma fatura que chega por correio torna-se um documento (1,5 min)

Escolha a mensagem de **Mariana Quadros**, a fatura FT 2026/0815, com o anexo `FT-2026-0815.txt`.

Carregue em **Guardar como documento**, ao lado do anexo. No diálogo, escolha o tipo
**«Por classificar»** e guarde.

Três coisas acontecem de uma vez:

- O anexo passa a ser um **documento a sério**, com referência própria, permissões e entrada de
  auditoria, criado pelo mesmo serviço que um carregamento manual usa. Não há porta lateral para o
  repositório.
- A mensagem passa a **Processada** e mostra a ligação ao documento que produziu. A ligação fica
  registada dos dois lados: meses depois ainda se responde a «de onde veio esta fatura».
- O painel **não salta** para outra mensagem, mesmo que ela tenha deixado de corresponder ao filtro
  «Não lida»/«Lida» que está ativo. O que se estava a ler continua no ecrã.

Siga a ligação para o documento. Repare que ele entrou **por classificar**, que é o estado correto,
porque ninguém o leu ainda. É exatamente onde a inteligência documental pega: abra o
separador de IA e o percurso continua, agora com um documento que chegou por correio em vez de ter
sido carregado à mão.

### 3. As ações que uma caixa de entrada precisa de ter (1 min)

Volte à caixa de entrada e escolha uma mensagem **sem anexo**, a renovação da parceria Nordvik, por
exemplo. As ações disponíveis mudam com o que a mensagem é:

- **Criar documento a partir da mensagem**: para as mensagens em que o corpo *é* o conteúdo. O
  documento guarda a mensagem com os seus cabeçalhos: quem enviou, quando, para que caixa. Um corpo
  sem isso não é prova de nada.
- **Associar a documento existente**: quando o assunto já tem documento e a mensagem é mais um
  episódio dele. A lista de documentos é a mesma, com as mesmas permissões: não se pode associar uma
  mensagem a um documento que não se pode abrir.
- **Iniciar processo**: só aparece depois de a mensagem ter documento, porque um processo corre
  sobre um documento. O servidor recusa na mesma se for pedido sem ele.

Experimente **Arquivar** e depois **Reabrir**: uma mensagem arquivada sai da lista de trabalho mas
fica no registo, e reabri-la devolve-a ao ponto onde estava: se já tinha produzido um documento,
volta a **Processada**, não a **Por ler**. O trabalho feito não se desfaz por se ter arquivado.

### 4. A caixa de uma organização é só dela (30 s)

Entre numa janela anónima como `admin@vertice.example`, o administrador da segunda organização.

A caixa de entrada está **vazia**, ou, se alguém já executou a integração de correio desta segunda
organização, tem as mensagens **dela**, nunca as da Asteria. Não é a interface a esconder: o filtro
de organização faz parte da consulta. Os filtros são declarados entidade a entidade, e por isso há um teste que enumera todas as
entidades com organização e falha se alguma ficar sem filtro: é o tipo de erro que só se evita com
uma rede.

## Centro de integrações (≈5 minutos)

Entre como `admin@asteria.example`, que tem `integrations.manage`. Abra **Integrações**.

### 1. Seis cartões, e o que cada um admite sobre si (1 min)

Seis sistemas: Microsoft Outlook, ERP, CRM, assinatura eletrónica, webhooks e importação de
ficheiros. Todos começam **«Por configurar»**, com tudo a zero, porque nenhum correu ainda, e um
ecrã que abrisse com números já lá postos estaria a inventá-los.

Repare em **quais** têm a faixa âmbar de simulação: o Outlook, o ERP e o CRM. Os outros três não.

Vale a pena parar aqui, porque é o ponto que distingue este ecrã de um mural de logótipos: **só é
dito simulado o que é simulado**. O lado de lá do ERP e do CRM é uma lista local fixa, e o correio
corre no conetor de demonstração, e esses dizem-no, e dizem exatamente *o quê*. Os outros três fazem
trabalho verdadeiro de ponta a ponta: a importação lê e move ficheiros reais do disco, os webhooks
entregam HTTP a um destino externo, a assinatura conta os documentos que estão mesmo à espera de
assinatura. Rotulá-los de demonstração faria parecer teatro o que é código a funcionar.

### 2. Executar o ERP encontra uma divergência verdadeira (1,5 min)

No cartão do **ERP**, carregue em **Executar agora**.

O cartão passa a **Ligada** e diz quantos registos reconciliou e quantos fornecedores estão por
registar. Abra **Ver registo**. A primeira linha é informativa: quantos valores foram conferidos,
em quantos documentos. As seguintes são **avisos**, uma por fornecedor que o ERP não conhece, com os
documentos afetados nomeados.

Um deles é **«Nordvik Systems»**: é o fornecedor de uma fatura que está mesmo no repositório, e está
deliberadamente ausente da lista do ERP simulado. Uma reconciliação que voltasse sempre limpa não
provaria nada e não ensinaria ninguém a ler o ecrã.

Note também que a divergência é um **aviso**, não um erro, e o contador de erros continua a zero. A
execução fez exatamente o que lhe foi pedido; o que ela encontrou é um achado, não uma falha. Use os
filtros de nível no topo do registo para ver só os avisos.

O mesmo vale para o **CRM**, sobre as contrapartes dos contratos em vez dos fornecedores das faturas.

### 3. A importação recusa-se a correr sem dono (45 s)

Execute o cartão da **Importação de ficheiros**. Sem nada na pasta vigiada, ele diz «nada por
importar» e qual é a pasta, como informação e não como erro.

O pormenor que vale a pena contar: o varrimento agendado do worker **salta** este cartão de
propósito, e diz porquê no registo. Um documento tem dono, e o trabalho agendado não tem sessão. As
alternativas eram inventar uma conta de sistema, que poria um nome fictício em cada documento
importado, ou atribuir tudo a um administrador qualquer. Recusar e explicar é mais honesto do que
qualquer das duas.

Para ver a importação a funcionar, ponha um ficheiro `.txt` em `src/Api/storage/import/<id da
organização>/` e execute de novo a partir do ecrã: o ficheiro entra como documento «Por classificar»
e o original é movido para `importados/`, para que uma segunda execução não o volte a importar.

### 4. O destino do webhook é verificado no servidor (1 min)

No cartão dos **Webhooks**, carregue em **Configurar** e escreva `http://exemplo.com/hook`. Guarde.

É recusado. Experimente agora `https://127.0.0.1/hook`: também é recusado, e por outra razão.

Aqui está a parte que interessa numa entrevista: um administrador a escrever um endereço que **este
servidor** vai depois chamar é a definição de um pedido forjado do lado do servidor (SSRF). Por isso
a verificação (HTTPS obrigatório, *loopback* e gamas privadas recusados) corre **no servidor e
antes de o endereço ser guardado**, não só no momento da entrega. O formulário valida também, mas só
para a mensagem chegar depressa; a verificação que conta é a que o cliente não pode contornar.

Guarde um endereço HTTPS externo e reabra o diálogo: o endereço está lá, lido da base de dados. Pode
mostrá-lo porque as definições de uma integração são **explicitamente não secretas**: nenhuma
credencial é guardada aqui, e é essa decisão que torna seguro mostrar o que lá está.

### 5. Ver e mudar são permissões diferentes (30 s)

Numa janela anónima, entre como `sofia.martins@asteria.example`, que tem `integrations.view` mas não
`integrations.manage`.

Os cartões estão todos lá, com os números e o registo, mas sem **Executar agora**, sem
**Configurar** e sem o interruptor de **Ativa**. Um gestor pode querer saber se o correio está a
chegar sem poder desligar nada. E não é a interface a decidir: as ações que ela esconde são recusadas
pelo servidor na mesma, com `403`.

### 6. Um processo que espera por um sistema externo (45 s)

Abra o **desenhador de workflows** e a paleta de nós. O nó **Integração** está lá,
executável como **todos os tipos de nó que o desenhador oferece**.

Ponha um nó de integração num processo e escolha qual integração ele corre. O que acontece quando o
processo lá chega vale a pena explicar: o nó **não** chama o sistema externo ali. Enfileira o pedido,
larga a transação do motor e a instância fica à espera; o worker corre a integração e retoma o
processo quando ela termina. Uma chamada de rede dentro da travessia do grafo manteria uma transação
de base de dados aberta à espera da Internet, e uma integração lenta passaria a ser um problema de
base de dados.

## Portal externo (≈4 minutos)

Entre como `contacto@northstar.example`. É a única conta de demonstração que não é de um colaborador
da organização: representa a **Northstar Supplies**, um fornecedor.

### 1. Uma porta diferente para quem vem de fora (45 s)

Repare para onde o início de sessão o leva: **`/portal`**, não `/app/dashboard`. E olhe para o que
não está lá: sem Documentos, sem Tarefas, sem Workflows, sem Caixa de entrada, sem sino de
notificações. Um convidado não deve conseguir deduzir, só pela moldura, que ecrãs a organização tem
por dentro.

O cabeçalho diz **Northstar Supplies**, não Asteria Group: um contacto que trabalhe com vários
clientes precisa de saber de que portal se trata.

Experimente escrever `/app/documents` na barra de endereço. Volta ao portal. Vale a pena dizer numa
entrevista que isto é **conveniência, não segurança**: o servidor recusa de qualquer forma, porque
nenhum perfil externo tem as permissões da aplicação interna. E corre nos dois sentidos: entre numa
janela anónima como `admin@asteria.example`, escreva `/portal`, e é devolvido ao painel. Nem o
administrador da organização entra no portal do fornecedor.

### 2. Submeter uma fatura (1 min)

Preencha o assunto, uma nota, escolha um ficheiro e submeta. A submissão aparece na lista como
**«Recebido»**.

O que acabou de acontecer do lado de dentro vale a pena contar:

- O ficheiro tornou-se um **documento a sério**, criado pelo mesmo `IDocumentService` que um
  carregamento manual usa, com referência própria, permissões e entrada de auditoria. Não há porta
  lateral para o repositório.
- Entrou como **«por classificar»**, e não como fatura. O tipo fatura exige fornecedor, número, data
  e total; um portal que os exigisse antes de aceitar o ficheiro seria uma porta de entrada pior do
  que o e-mail que veio substituir. É também exatamente onde a inteligência documental o
  apanha.
- Entrou no **departamento financeiro, com acesso de contribuidor**. Sem isso a submissão chegava e
  ficava intocável: o tipo de receção é confidencial, e um documento confidencial sem departamento
  é visível só ao dono. O fornecedor teria posto o documento numa vitrina.

### 3. O que o fornecedor vê, e o que não vê (1 min)

Abra a submissão. Tem o que enviou, quando, o ficheiro, o estado, e uma secção de **perguntas e
respostas** que diz, por extenso, que as notas internas ficam do lado de dentro.

Agora o contraste. Noutra janela, entre como `ricardo.lopes@asteria.example` e inicie um processo
sobre esse documento. O processo tem cronologia, tarefas, prazos, responsáveis. Volte ao portal: o
fornecedor continua a ver **nada disso**.

Não é a interface a esconder. A lista que o portal constrói **seleciona dois tipos de evento pelo
nome** (a pergunta e a resposta) em vez de mostrar tudo menos o que é interno. A diferença importa:
uma lista construída por exclusão deixaria entrar, por omissão, o tipo de evento que alguém
acrescentar a seguir. Selecionar falha fechado; excluir falha aberto.

### 4. A conversa que o portal existe para ter (1 min)

Como `ana.ferreira@asteria.example` (a tarefa de validação da fatura é de Compras), abra a tarefa e
carregue em **Pedir informação**, escrevendo uma pergunta ao fornecedor.

De volta ao portal, a submissão mostra **«Aguarda a sua resposta»** e o detalhe traz a pergunta, com
uma caixa para responder. Responda.

Três coisas a apontar:

- A resposta entra na **cronologia interna do processo** e **notifica quem perguntou**: quem pediu
  a informação não tem de ir verificar se ela chegou.
- A resposta **não faz o processo avançar**. Acrescenta informação; a decisão continua de quem
  perguntou. Um fornecedor não aprova a sua própria fatura respondendo a uma questão sobre ela.
- A marca «aguarda a sua resposta» **desaparece** assim que responde, embora a tarefa continue aberta
  do lado de dentro. Ler o estado da tarefa deixaria a marca acesa para sempre; o que interessa ao
  fornecedor é se a última coisa dita foi uma pergunta.

### 5. E só o que é seu (15 s)

Volte à lista. Estão lá a fatura que a organização lhe partilhou e o que ele próprio submeteu, e
mais nada do repositório, que tem dezenas de documentos.

O portal **não tem regras de autorização próprias**: usa o mesmo avaliador de acesso da aplicação
interna, que já dizia que um utilizador externo alcança um documento apenas por propriedade ou por
concessão explícita. Dois conjuntos de regras sobre os mesmos documentos acabariam por divergir, e
quando divergem, é sempre o mais permissivo que ganha.

## Assinatura eletrónica (≈3 minutos)

> **Diga isto em voz alta antes de mostrar o ecrã:** esta funcionalidade **não tem validade legal**.
> Não é uma assinatura qualificada nem avançada: não há chave privada sob controlo do signatário,
> nem certificado, nem carimbo temporal independente. É um registo de que alguém concordou com um
> conteúdo identificado. O ecrã diz o mesmo, no topo, antes de tudo o resto.

### 1. Pedir (1 min)

Como `admin@asteria.example`, abra um documento aprovado e vá ao separador **Assinaturas**.

A primeira coisa visível é o aviso. É deliberado que ocupe espaço: tudo o que está por baixo
parece-se exatamente com uma funcionalidade que produz assinaturas juridicamente vinculativas.

Escolha quem assina e peça. Repare no que acontece ao documento: passa a **«Aguarda assinatura»**, e
o pedido mostra a **versão** e o **resumo SHA-256 completo** do conteúdo.

Experimente escolher alguém que não tenha acesso ao documento. É recusado, com a indicação de que é
preciso conceder-lhe acesso primeiro: um pedido que a pessoa nunca poderia cumprir ficaria na lista
dela a devolver recusas para sempre.

### 2. Só quem foi nomeado assina (45 s)

Ainda como administrador, que detém **todas** as permissões da organização, tente assinar. É
recusado.

Vale a pena parar aqui numa entrevista. Não é uma falha de permissões: é a afirmação de que uma
assinatura é a declaração de uma pessoa sobre um documento, e não existe sentido útil em que seja
feita por procuração. Nenhuma antiguidade contorna isto.

Entre com a conta do signatário e assine. O documento passa a **«Assinado»** e o registo ganha a
data e o resumo do conteúdo assinado.

### 3. O que torna o registo verificável (1 min)

Este é o ponto técnico da funcionalidade.

O pedido **fixou a versão** e copiou o resumo do conteúdo. Se alguém carregar uma versão nova, o
registo **não é apagado nem re-apontado**: passa a mostrar que já não corresponde ao conteúdo atual.

São três leituras possíveis e só uma é verdadeira: apagar seria perder prova, re-apontar em silêncio
seria falsificá-la, e dizer «já não corresponde» é o que de facto aconteceu.

E a verificação não depende de confiar na aplicação: descarregue o ficheiro, calcule-lhe o SHA-256 e
compare com o que está no ecrã.

### 4. O histórico (15 s)

Abra o separador **Auditoria** do mesmo documento. Lá estão `signature.requested` e
`signature.signed`, com quem, quando e sobre que versão, no sítio onde as pessoas procuram a
história de um documento, e não escondidas num registo separado de assinaturas.

## Análises (≈4 minutos)

Entre como `admin@asteria.example` e abra **Análises**.

### 1. Os números não estão escritos em lado nenhum (1 min)

Repare primeiro no que o painel diz de si próprio: os números contam **apenas o que quem está a ver
tem permissão para ver**. Não é uma limitação técnica: é a mesma regra da listagem e da pesquisa,
aplicada aqui. Uma contagem é informação: saber quantos documentos confidenciais existem no
jurídico é saber alguma coisa sobre eles.

Mude o período para **7 dias** e veja os números mudarem. Se não mudassem, o filtro não estaria a
chegar ao servidor, e é exatamente esse o teste que corre nesta página.

### 2. O que o painel se recusa a dizer (1 min)

Ainda com a janela de sete dias, procure uma medida que diga **«sem base de cálculo no período»**.

Esta é a parte que vale a pena explicar em voz alta: quase todos os painéis mostrariam `0%` ali.
Zero é uma afirmação (mediu-se e deu zero), e num período em que nada aconteceu lê-se como um mau
resultado quando não houve resultado nenhum. É uma mentira com aspecto de facto. O contrato da API
devolve `null`, e o ecrã escreve-o por extenso ([ADR-010](adr/ADR-010-analytics-from-queries.md)).

### 3. Um gráfico por medida (1 min)

Documentos criados, processos iniciados e processos concluídos têm **três gráficos separados**, não
um com dois eixos. Medidas de escalas diferentes num eixo duplo fazem parecer comparável o que não
é, e o problema não é estético, é de leitura.

Desça até **«Onde os processos param»**: os passos com trabalho à espera, com nome real e há quanto
tempo. É a pergunta que um gestor faz, respondida com uma consulta em vez de uma impressão.

### 4. Exportar (1 min)

Exporte o CSV. Repare que os estados vão em nomes legíveis, e não em números: um ficheiro não tem
quem o traduza do lado de lá.

Depois saia e entre como `ana.ferreira@asteria.example`. O botão de exportar **desapareceu**: exige
`reports.export`, que este perfil não tem. Se o forçasse pelo endereço, o servidor recusava na mesma:
esconder é conveniência, a recusa é do servidor.

## Administração (≈4 minutos)

Como `admin@asteria.example`, abra **Administração**.

### 1. Criar uma conta que entra mesmo (1 min 30 s)

Crie uma pessoa: e-mail, nome, um perfil e um departamento. A palavra-passe inicial aparece **uma
vez**, num aviso. Copie-a.

Não há botão para a ver outra vez, e isso é a funcionalidade: o servidor guarda apenas o resumo
criptográfico. Um ecrã capaz de a mostrar de novo estaria a dizer que a guarda em claro.

Saia, entre com essa conta. Funciona. Volte a entrar como administrador, **desative-a**, e tente
entrar outra vez: já não entra. Ligue **«incluir desativadas»**: a conta continua lá. Não foi
eliminada, porque a auditoria e os documentos referem-se a ela.

### 2. A mudança que o ecrã recusa fazer (1 min)

Procure a sua própria linha na lista. **Não tem botões**: diz «a sua conta».

Explique porquê: um administrador que se retire o perfil de administração, ou se desative, cria a
única avaria sem reparação possível de dentro do produto. O servidor recusa mesmo que o pedido
chegue por outra via; o que a interface faz é não oferecer o que vai ser recusado.

Procure agora `admin@docops.example`. Também não tem botões, mas a legenda é outra: **«conta de
plataforma»**. As duas são inalteráveis daqui, por razões diferentes, e uma única legenda para as
duas seria falsa numa delas.

### 3. O perfil que não se distribui (1 min)

Repare nos perfis oferecidos: `TenantAdmin`, `Manager`, `Employee`, `FinanceApprover`,
`ExternalPartner`, e **não** `SystemAdmin`. Esse é concedido ao nível da plataforma; se uma
organização o pudesse atribuir, promovia-se para fora da sua própria fronteira. Não está na lista
**e** é recusado com `409` se for enviado à mão.

### 4. Departamentos (30 s)

Crie um departamento e tente criar outro com o mesmo código. É recusado, com a mensagem a vir do
servidor: é lá que a unicidade é decidida, não numa validação do browser.

## Catálogo de documentos (≈4 minutos)

Como `admin@asteria.example`, abra **Administração** e o separador **Catálogo**.

> **A frase a dizer antes de mostrar:** este ecrã parece configuração, mas não é. Já existem
> documentos classificados por estes tipos e descritos por estes campos. Cada alteração aqui muda o
> significado do que já está escrito, e é por isso que o servidor recusa metade do que se lhe pede.

### 1. O que o catálogo já produziu (1 min)

Repare nas colunas **Campos** e **Documentos**: não são decoração, são o que torna as recusas
explicáveis. «Este campo não pode mudar» não é uma resposta; «8 documentos têm valor nele» é.

### 2. A recusa que interessa (1 min 30 s)

Abra os **Campos** de *Fatura de fornecedor*. Repare que os campos preenchidos **não têm botão de
eliminar**: no lugar dele está o número de documentos que os usam.

Agora crie um campo novo e marque-o como **obrigatório**. É recusado, e a mensagem diz quantos
documentos ficariam inválidos.

Explique porquê: tornar um campo obrigatório é uma afirmação sobre documentos que já existem. Se
algum não o cumpre, a afirmação é falsa no instante em que é guardada, e o catálogo deixa de poder
ser lido como verdade.

### 3. A porta que não se fecha (1 min)

Volte à lista e procure **Por classificar**, marcado como **Entrada**. Não tem botão de desativar:
diz «sempre ativo».

É por este tipo que entra um ficheiro antes de alguém saber o que é, incluindo os que chegam pela
caixa de entrada e pelo portal, onde ninguém está a olhar. Desativá-lo travaria a entrada de
documentos sem ninguém dar por isso. Pela mesma razão não aceita campos obrigatórios: seria recusar
o carregamento até alguém responder a uma pergunta que só ler o documento responde.

### 4. O que é permitido (30 s)

Crie um tipo novo, dê-lhe um campo, e apague-o. Enquanto ninguém o preencheu, apagar não destrói
nada, e por isso é permitido. A regra não é «não se mexe»; é **alargar pode, estreitar não**.

## Etiquetas e retenção (≈4 minutos)

Como `admin@asteria.example`, abra **Administração** e o separador **Etiquetas**.

### 1. Remover sem destruir (1 min 30 s)

Escolha uma etiqueta com documentos e **remova-a**. Desaparece da lista.

Ligue **«incluir removidas»**: continua lá, marcada como removida. **Restaure-a** e repare no
número de documentos: voltou ao que era.

Esta é a parte que vale a pena explicar. No separador **Catálogo**, eliminar um campo de metadados
com valores é **recusado**. Aqui, remover uma etiqueta é **permitido**. Não é incoerência: um valor
de metadados é o que o documento *afirma* (o número da fatura, a data), e destruí-lo destrói o que
o documento diz. Uma etiqueta é uma anotação *colocada sobre* ele por quem o organiza. A primeira
não se recupera de lado nenhum; a segunda está a um sinalizador de distância.

### 2. Uma cor que não chega (30 s)

Crie uma etiqueta e escreva `verde` no campo da cor. É recusado, com a indicação de que tem de ser
hexadecimal.

Reparar nisto é o ponto: o caminho fácil seria ignorar o valor e guardar sem cor. Ficaria alguém à
procura de uma cor que escolheu e nunca viu aplicada.

### 3. Quando mudar **não** é falsificar (1 min 30 s)

Passe ao separador **Retenção**.

Escolha uma política e **mude a duração**. É permitido, e é o contrário do que o catálogo faz com
um campo de metadados. A diferença: uma obrigação de retenção que muda é um facto, não uma
falsificação. Muda a lei, muda o contrato; um catálogo incapaz de a acompanhar é que estaria a
mentir. O que a mudança faz é mexer na data de eliminação de todos os documentos que a política
governa, e por isso fica na auditoria com o valor antigo e o novo.

Agora tente **retirar** a política *Contratos (10 anos após término)*. É recusado, e a mensagem
nomeia o tipo que a adota. Retirá-la deixaria esse tipo a aplicar uma regra que a organização
decidiu abandonar, sem nada no ecrã a dizê-lo.

### 4. Quem decide o quê (30 s)

Entre como `sofia.martins@asteria.example`. Os separadores **Etiquetas** e **Retenção** não
aparecem. Gerir etiquetas exige `admin.documenttypes`; decidir quanto tempo se guardam documentos
exige `admin.settings`, porque conformidade e classificação são decisões diferentes, e o servidor recusa
as duas na mesma.

## Editor de permissões (≈4 minutos)

Abra um documento e vá ao separador **Permissões**. Esta é a demonstração que mais peso costuma ter
numa avaliação técnica, porque autorização é onde os produtos empresariais falham em silêncio.

### 1. Conceder (1 min 30 s)

Conceda acesso a um **departamento**, depois mude o tipo para **perfil** e para **utilizador**. A
lista de perfis **vem do servidor**, e é a mesma que a
camada de autorização usa. Uma lista escrita no ecrã divergiria, e seria a do ecrã que ninguém
repararia que estava errada.

Repare que **`SystemAdmin` não aparece**. Está acima da organização; conceder-lhe um documento seria
dar acesso a uma conta que esta organização não gere.

Preencha **«expira em»** com uma data futura. É a janela de um auditor externo. Tente uma data
passada: é recusada, porque uma janela que fechou antes de abrir não é uma janela.

### 2. A pergunta que interessa (2 min)

Desça até **«Porque é que alguém alcança este documento»** e escolha uma pessoa.

Explique isto em voz alta: mostrar o acesso **de quem está a olhar** responde a metade da pergunta.
Quem administra precisa de saber porque é que **outra** pessoa alcança, ou não alcança, um
documento. O painel mostra a avaliação regra a regra: o que a posse concede, o que o departamento
concede, o que uma concessão explícita acrescenta, e o que uma restrição retira.

**E não é um cálculo à parte.** É o mesmo `DocumentAccessEvaluator` que decide de verdade quando
alguém tenta abrir ou descarregar o documento. Um painel que computasse a sua própria resposta
acabaria por discordar da regra que diz explicar, e a discordância só se descobriria quando alguém
alcançasse o que não devia.

### 3. Quem pode perguntar (30 s)

Entre como `ana.ferreira@asteria.example` e abra o mesmo separador. Não há formulário de concessão
nem seletor de pessoa. E se forçasse o pedido, o servidor recusava: perguntar «esta pessoa
conseguiria fazer isto?» é legítimo para quem decide conceder, e mais ninguém.

## Saúde do sistema (≈3 minutos)

Como `admin@asteria.example`, abra **Administração** e o separador **Sistema**.

### 1. O que o produto sabe sobre si próprio (1 min 30 s)

Os quatro trabalhos em segundo plano aparecem com a última corrida, o desfecho, quantas vezes
correram nas últimas 24 horas e quanto demoraram em média.

A parte que vale a pena explicar é **porque é que isto existe em tabela**. As métricas do worker
saem por OpenTelemetry, que é o sítio certo para elas, e que é outro processo. Sem uma linha
escrita na base de dados, «o trabalho periódico correu esta noite?» é uma pergunta que a aplicação
não sabe responder sobre si própria.

Repare também que **os quatro aparecem sempre**, mesmo os que nunca correram. Um trabalho ausente do
ecrã e um trabalho que nunca correu parecem o mesmo a quem lê. Quem nunca correu diz «nunca correu»,
e a duração média fica a traço, e não a zero, que se leria como «instantâneo».

### 2. O que as sondas públicas dizem, e o que não dizem (1 min)

Abra `/health` numa janela sem sessão iniciada. Responde, e devolve **nome, estado e duração**. Mais
nada: nem mensagem de exceção, nem cadeia de ligação, nem nome de máquina.

Uma sonda de vida que descreve a falha é reconhecimento gratuito para quem está a sondar o
sistema. O detalhe (qual trabalho falhou, o que disse) está no separador **Sistema**, atrás de permissão.

### 3. Quem opera não é quem administra (30 s)

Entre como `sofia.martins@asteria.example`. O separador **Sistema** não aparece: ver o estado da
plataforma exige `admin.settings`, e administrar pessoas é `admin.users`. São trabalhos diferentes, e
o servidor recusa na mesma.

## Estados vazios que não inventam a causa (≈2 minutos)

Entre como `admin@asteria.example` e vá a **Tarefas**, sem tocar em filtro nenhum.

O ecrã está vazio. Esta conta tem tarefas, mas nenhuma em aberto, e a organização tem oito à espera
de alguém. Os dois filtros que vêm postos por omissão, «as minhas» e «apenas em aberto», bastam
para esconder esse trabalho, e uma frase como *«Quando um processo lhe atribuir trabalho, aparece
aqui.»* levaria quem a lê a acreditar e a sair.

Por isso a frase nomeia o que está posto («está a ver só as suas tarefas e só as que continuam por
fechar») e admite as duas leituras possíveis: pode não haver trabalho, ou podem ser os filtros.
Carregue em **Alargar os filtros**: aparecem trinta e três tarefas.

Vale a pena dizer porque é que o botão só aparece a algumas pessoas. Entre como
`ana.ferreira@asteria.example`, que não tem `tasks.view.all`, e vá a **Tarefas → Delegadas por mim**,
tirando «apenas em aberto». Também está vazio, e **não há botão nenhum**: ela já está a ver tudo o
que lhe é permitido, e um botão que não mudasse nada seria um botão sem ação. A frase muda em
conformidade: «ainda não delegou nenhuma tarefa a outra pessoa».

---

## Vistas partilháveis e verificação de retenção (≈3 minutos)

Entre como `admin@asteria.example` e vá a **Documentos**.

Filtre: escreva «contrato» na pesquisa e carregue em **Aguarda aprovação**. **Olhe para a barra de
endereço**: mudou para `?q=contrato&statuses=…`. Copie o endereço, abra um separador novo e cole-o: a
mesma vista abre lá, com os mesmos filtros marcados e os mesmos documentos.

É a diferença entre uma ferramenta e um ecrã. «As faturas por aprovar do departamento financeiro» é
uma coisa que se diz a um colega, e o endereço é o que se lhe envia. Se os filtros vivessem só
dentro daquele separador, recarregar perdê-los-ia.

Recarregue a página para ver que a vista sobrevive. Depois estrague o endereço de propósito
(`?page=abc&statuses=nao-e-numero`): a listagem continua a ser uma listagem, na primeira página. Um
endereço cortado a meio por um cliente de correio chega assim, e não é motivo para partir o ecrã.

**A retenção, do outro lado, não se vê num botão, e é esse o ponto.** Em `/app/admin`, separador
**Retenção**, estão as políticas que dizem quanto tempo cada tipo de documento se conserva; ao
arquivar, a data de fim é calculada e guardada. Uma data que ninguém volta a ler não produziria nada no dia
em que o prazo passasse.

Por isso o worker verifica-as, e quando um prazo termina **notifica quem administra a organização e não
elimina nada**. Vale a pena dizer isto em voz alta numa entrevista: a tentação é agendar a
eliminação, e seria errado. Um prazo cumprido significa que a organização *pode* dispor do documento,
não que deva: a base legal que obrigou a guardá-lo é a mesma que faz da destruição uma decisão que
alguém assina. Fica notificação e fica auditoria; a pessoa decide.

O mesmo documento não é anunciado duas vezes: o que já foi avisado reconhece-se pela existência da
própria notificação. Um aviso que se repetisse a cada ciclo do worker deixaria de ser lido dentro de
uma hora, e o documento ficaria por eliminar na mesma.

---

## Cenário completo: fatura de fornecedor

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

## Cenário completo: ciclo de vida de um contrato

1. Carregar um contrato.
2. Extrair metadados, incluindo a data de expiração.
3. Iniciar a revisão jurídica (Carla Nunes).
4. Aprovação de direção (Helena Vasconcelos).
5. Pedido de assinatura eletrónica (**demonstração, sem validade legal**).
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
| Nó de integração espera fora da transação | Nenhuma chamada lenta a um sistema externo prende a base de dados |
| Ações da tarefa vindas do servidor | A interface reflete a autorização, não a define |
| Endereço de uma vista filtrada | O estado da interface é partilhável, não preso a um separador |
| Retenção que avisa e não elimina | Saber onde a automação tem de parar e devolver a decisão a uma pessoa |
| Estado vazio que nomeia os filtros | Um ecrã não afirma uma causa que não conhece |
