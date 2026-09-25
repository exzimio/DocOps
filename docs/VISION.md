# DocOps Control Tower

**Plataforma de gestão documental e automação de processos para organizações.**

Projeto de portefólio independente. Demonstração técnica pessoal, sem afiliação a qualquer
produto ou fornecedor comercial. Todas as organizações, pessoas e documentos são fictícios.

## 1. O que é

Uma plataforma empresarial completa onde documentos entram, são classificados, percorrem
circuitos de aprovação, são assinados, pesquisados, auditados e medidos.

Não é um protótipo nem uma maqueta. É software que funciona contra uma base de dados real:
todos os botões fazem alguma coisa, todos os números vêm de uma consulta, e o projeto tem uma
regra explícita que proíbe páginas de marcador de posição, secções «em breve» e funcionalidade
por acabar.

## 2. O problema que resolve

Nas organizações, os documentos que importam — contratos, faturas, propostas, pareceres —
vivem espalhados por correio eletrónico, pastas partilhadas e sistemas que não falam uns com
os outros. Ninguém sabe onde está o processo, quem falta aprovar, nem quanto tempo demorou.

O DocOps Control Tower centraliza esse ciclo de vida: da entrada do documento até ao arquivo,
com quem pode ver o quê decidido pelo servidor, um trilho de auditoria que a própria aplicação
não consegue alterar, e indicadores que saem dos dados reais em vez de folhas de cálculo.

## 3. O que já faz

**Gestão documental.** Versões, metadados, tipos de documento, etiquetas. Permissões ao nível
do documento cruzadas com departamentos e perfis. Pré-visualização e descarga controladas.

**Motor de workflow.** Desenhador visual de circuitos de aprovação. Passos sequenciais e
paralelos, prazos, escalonamento automático por serviço em segundo plano, notificações,
reatribuição e histórico completo de decisões.

**Inteligência artificial aplicada.** Classificação de documentos, extração de campos e
sumarização, com nível de confiança e revisão humana das extrações. O fornecedor de
demonstração é determinista e está identificado como tal em todo o lado onde aparece: o
projeto proíbe apresentar uma resposta de demonstração como saída de um modelo real, ou
inventar citações.

**Pesquisa.** Texto integral sobre o conteúdo, filtros combinados e pesquisa em linguagem
natural — sempre restrita ao que quem pesquisa tem direito a ver.

**Caixa de entrada e integrações.** Conetor de correio com sincronização agendada. Seis
adaptadores de integração (ERP, CRM, Outlook, importação de ficheiros, assinatura, webhooks)
com fila de execução, repetição em caso de falha e registo consultável de cada corrida.

**Portal externo.** Parceiros e fornecedores submetem documentos sem terem acesso ao produto.
As submissões entram no circuito interno com as permissões certas atribuídas.

**Assinatura eletrónica.** Pedido, assinatura pelo signatário nomeado, cancelamento e deteção
de alteração do conteúdo depois do pedido. Demonstração de portefólio — o projeto declara
explicitamente que não reclama validade legal.

**Análises e relatórios.** Indicadores de volume, tempo médio de processamento, taxa de
automação, estrangulamentos por passo de workflow, distribuições por estado, tipo e
departamento. Gráficos construídos à mão em SVG. Exportação para CSV. Quando não há base de
cálculo, o painel diz que não há — não mostra zero.

**Administração.** Criação e desativação de contas, atribuição de perfis e departamentos,
gestão de departamentos. 38 permissões distribuídas por 6 perfis.

**Transversal.** Auditoria imutável, multi-organização, português europeu e inglês, tema claro
e escuro, acessibilidade, interface funcional em telemóvel.

## 4. Como está construído

- **Backend** — .NET 10 / C# 14, monólito modular em quatro camadas (Domínio, Aplicação,
  Infraestrutura, API), mais um serviço de trabalho periódico.
- **Dados** — SQL Server, Entity Framework Core, 28 tabelas.
- **Frontend** — React 19, TypeScript, Tailwind, TanStack Query, i18next.
- **Testes** — xUnit, Testcontainers (SQL Server real em contentor), Playwright.
- **Ambiente** — Docker Compose.

## 5. Onde está a complexidade real

Estes são os problemas que separam uma aplicação de demonstração de software que aguenta
produção.

**1. Isolamento entre organizações imposto na persistência.** Cada consulta é filtrada pela
organização de quem pede, vinda das claims do cookie e nunca do pedido. Não há parâmetro por
onde um utilizador alcance dados de outra organização. Existe um teste que falha a compilação
se alguém acrescentar uma entidade sem esse filtro — a rede não depende de ninguém se lembrar.

**2. Autorização em camadas, sempre no servidor.** O que a interface esconde é conveniência,
nunca segurança. As permissões do perfil funcionam como um teto sobre o que a posse de um
documento concede: ser dono não dá mais do que o perfil permite.

**3. Um predicado de visibilidade único, traduzível para SQL.** A mesma regra de «o que esta
pessoa pode ver» é reutilizada na pesquisa, no portal e nos relatórios. Sem isso, um painel
poderia revelar por contagem a existência de documentos que a pessoa não pode abrir — uma fuga
subtil que este projeto teve de corrigir uma vez e depois fechou na origem.

**4. Assinar não é editar.** Descoberta durante a construção: o fluxo de assinatura exigia
permissão de edição por reutilizar o caminho de mudança de estado. Um signatário externo
passaria a poder alterar o documento. Resolvido com um caminho próprio, e documentado numa
decisão de arquitetura.

**5. Testes que recusam padrões frágeis.** Um padrão de teste repetiu-se três vezes e partiu a
suíte. Em vez de o documentar como armadilha conhecida, foi escrita uma guarda automática que
lê o código dos próprios testes e falha a compilação quando o padrão reaparece — validada
injetando a falha de propósito para confirmar que é apanhada.

**6. Defeitos encontrados a olhar, não a ler.** Mais de dez defeitos reais foram apanhados por
chamadas à API verdadeira e por leitura de capturas de ecrã, não por revisão de código.
Exemplos: um administrador de organização conseguia editar a conta de administração da
plataforma; um ecrã explicava uma recusa com o motivo errado.

## 6. Disciplina de engenharia

Cada fase segue a mesma ordem, sem exceções: backend primeiro, validação com chamadas reais à
API, depois interface, verificação no browser com leitura das capturas de ecrã, testes,
documentação, e só então o commit.

Antes de cada commit correm oito portões — compilação sem qualquer aviso, testes unitários,
testes de integração contra SQL real, verificação de tipos, análise estática, formatação,
testes de componentes e testes end-to-end em desktop e telemóvel.

Nove decisões de arquitetura estão registadas por escrito (ADR), incluindo as que explicam
porque algo **não** foi feito — por exemplo, porque a assinatura eletrónica não gerou um par
de chaves criptográficas: seria mais convincente e continuaria sem valor legal, a pior
combinação possível.

## 7. Números

- ~60.700 linhas de C# e ~17.700 linhas de TypeScript/React
- 104 endpoints REST em 15 controladores
- 43 ficheiros de domínio, 28 tabelas
- 327 testes unitários
- 151 testes de integração contra SQL Server real
- 59 testes de componentes
- 20 suítes end-to-end, corridas em desktop e telemóvel
- 9 decisões de arquitetura registadas
- 38 permissões distribuídas por 6 perfis

## 8. Estado atual

Planeado em dez fases, cada uma entregue a funcionar de ponta a ponta.

Fases 1 a 7 concluídas. Fase 8 em curso — análises concluídas, administração de pessoas e
departamentos concluída; falta a administração de tipos de documento, campos de metadados,
políticas de retenção e o editor de permissões. Seguem-se as fases 9 e 10.

Nenhuma fase foi dada por fechada sem estar a funcionar, testada e documentada.

## 9. O que este projeto demonstra

Que sei construir um sistema empresarial completo — não uma funcionalidade isolada — com as
decisões difíceis tomadas conscientemente e escritas: segurança imposta no servidor,
isolamento de dados entre clientes, testes que provam comportamento em vez de confirmarem
implementação, e honestidade sobre o que o software é e o que não é.
