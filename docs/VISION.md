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

## 3. O que faz

**Gestão documental.** Versões, metadados, tipos de documento, etiquetas, políticas de retenção.
Permissões ao nível do documento cruzadas com departamentos e perfis. Pré-visualização e descarga
controladas.

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

**Administração.** Contas, perfis e departamentos. Catálogo de tipos de documento e dos campos
de metadados de cada um. Etiquetas e políticas de retenção. Editor de permissões com um painel
que explica **porque é que** uma pessoa alcança — ou não alcança — um documento.

**Operação.** Estado do trabalho em segundo plano, trabalho à espera, métricas por
OpenTelemetry, e sondas de saúde que não descrevem falhas a quem não tem sessão.

**Transversal.** Auditoria imutável, multi-organização, português europeu e inglês, tema claro
e escuro, acessibilidade verificada contra a norma, interface funcional em telemóvel.

## 4. Como está construído

- **Backend** — .NET 10 / C# 14, monólito modular em quatro camadas (Domínio, Aplicação,
  Infraestrutura, API), mais um serviço de trabalho periódico.
- **Dados** — SQL Server, Entity Framework Core, 29 tabelas, 12 migrações.
- **Frontend** — React 19, TypeScript, Tailwind, TanStack Query, i18next.
- **Testes** — xUnit, Testcontainers (SQL Server real em contentor), Vitest, Playwright, axe.
- **Observabilidade** — Serilog, OpenTelemetry, métricas próprias do produto.
- **Ambiente** — Docker Compose.

## 5. Onde está a complexidade real

Estes são os problemas que separam uma aplicação de demonstração de software que aguenta
produção. Todos foram encontrados durante a construção, e todos estão documentados com a causa.

**1. Isolamento entre organizações imposto na persistência.** Cada consulta é filtrada pela
organização de quem pede, vinda das *claims* do cookie e nunca do pedido. Existe um teste que
falha a suite de testes se alguém acrescentar uma entidade sem esse filtro — a rede não depende de
ninguém se lembrar.

**2. E uma fuga que aconteceu mesmo assim.** `IgnoreQueryFilters()` é um operador de consulta,
não de subconsulta: escrito dentro de uma projeção, desliga os filtros da consulta inteira,
incluindo o da organização. Um administrador passou a ver as etiquetas de outra empresa. Não
falhava nada — nem exceção, nem aviso, nem teste vermelho. Hoje há uma guarda que lê o
código-fonte e falha a suite de testes se o padrão reaparecer.

**3. Autorização em camadas, sempre no servidor.** As permissões do perfil funcionam como um
teto sobre o que a posse de um documento concede: ser dono não dá mais do que o perfil permite.
O painel que explica o acesso não calcula nada — mostra a mesma avaliação que o servidor usa
quando decide, porque uma segunda verdade sobre autorização diverge sempre.

**4. Alargar pode, estreitar não.** Um campo de metadados com valores não muda de tipo nem se
elimina; uma etiqueta remove-se, porque é uma anotação e não algo que o documento afirme; uma
política de retenção muda de duração, porque as obrigações mudam de facto. Três respostas
diferentes para perguntas que parecem iguais.

**5. Assinar não é editar.** O fluxo de assinatura exigia permissão de edição por reutilizar o
caminho de mudança de estado — um signatário externo passaria a poder alterar o documento.

**6. Testes que recusam padrões frágeis.** Três guardas leem o código-fonte e falham a
compilação: entidades sem filtro de organização, testes que afirmam contagens fixas, e o
operador que desliga filtros dentro de projeções. Cada uma nasceu de um defeito real.

**7. O que nenhuma captura de ecrã mostrava.** O contraste de texto esteve 0,4 abaixo da norma
durante nove fases. Renderiza perfeitamente; só um cálculo o distingue. Apareceu no dia em que
uma auditoria automática passou a correr sobre dez ecrãs.

## 6. Disciplina de engenharia

Cada fase segue a mesma ordem, sem exceções: backend primeiro, validação com chamadas reais à
API, depois interface, verificação no browser com leitura das capturas de ecrã, testes,
documentação, e só então o commit.

Antes de cada commit correm nove portões — compilação sem qualquer aviso, testes unitários,
testes de integração contra SQL real, verificação de tipos, análise estática, formatação nos
dois pacotes, testes de componentes e testes end-to-end em desktop e telemóvel, a partir de uma
base de dados reposta.

Dez decisões de arquitetura estão registadas por escrito (ADR), incluindo as que explicam
porque algo **não** foi feito — por exemplo, porque a assinatura eletrónica não gerou um par
de chaves criptográficas: seria mais convincente e continuaria sem valor legal, a pior
combinação possível.

## 7. Números

- ~28 000 linhas de C# e ~17 700 de TypeScript/React, mais ~11 200 de testes
- 123 endpoints REST em 17 controladores
- 45 ficheiros de domínio, 29 tabelas, 12 migrações
- **748 testes**: 330 unitários, 181 de integração contra SQL Server real, 59 de componentes e
  178 end-to-end em desktop e telemóvel
- 3 guardas que leem o código-fonte e falham a suite de testes
- 10 decisões de arquitetura registadas
- 44 permissões distribuídas por 6 perfis

## 8. Estado

Construído em dez fases, cada uma entregue a funcionar de ponta a ponta. **Nove concluídas**; a
décima — refinamento de UX, acessibilidade, desempenho e documentação — está em curso, com a
auditoria de acessibilidade e a divisão de pacotes já feitas.

Nenhuma fase foi dada por fechada sem estar a funcionar, testada e documentada.

## 9. O que este projeto demonstra

Que sei construir um sistema empresarial completo — não uma funcionalidade isolada — com as
decisões difíceis tomadas conscientemente e escritas: segurança imposta no servidor,
isolamento de dados entre clientes, testes que provam comportamento em vez de confirmarem
implementação, e honestidade sobre o que o software é e o que não é.

E que, quando um erro aparece, a correção vai à origem: três dos defeitos encontrados deixaram
de ser possíveis, porque passaram a falhar a suite de testes.
