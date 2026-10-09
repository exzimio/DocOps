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

**6. Código que recusa padrões frágeis.** Um analisador de Roslyn falha a **compilação** quando
alguém desliga os filtros de organização dentro de uma projeção — o defeito que causou uma fuga
verdadeira entre organizações. Dois testes leem o código-fonte e falham a **suite**: entidades sem
filtro de organização, e testes que afirmam contagens fixas. Cada um nasceu de um defeito real, e
cada um foi validado a reintroduzir esse defeito.

**7. O que nenhuma captura de ecrã mostrava.** O contraste de texto esteve 0,4 abaixo da norma
durante nove fases. Renderiza perfeitamente; só um cálculo o distingue. Apareceu no dia em que
uma auditoria automática passou a correr sobre dez ecrãs.

## 6. Disciplina de engenharia

Cada funcionalidade segue a mesma ordem: backend primeiro, validação com chamadas reais à API,
depois interface, verificação no browser com leitura das capturas de ecrã, testes, documentação, e
só então o commit.

Antes de cada commit correm os mesmos portões: compilação sem qualquer aviso, testes unitários,
testes de integração contra SQL real, verificação de tipos, análise estática, formatação nos dois
pacotes, testes de componentes e testes end-to-end em desktop e telemóvel, a partir de uma base de
dados reposta.

As decisões de arquitetura estão registadas por escrito em ADR, incluindo as que explicam porque
algo **não** foi feito. A assinatura eletrónica, por exemplo, não gera um par de chaves
criptográficas: seria mais convincente e continuaria sem valor legal, a pior combinação possível.

## 7. Números

- Cerca de 29 000 linhas de C#, 18 000 de TypeScript e React, e 12 500 de testes
- Mais de 120 endpoints REST, em 17 controladores
- Cerca de 30 tabelas
- **Mais de 800 testes automatizados**: unitários, de integração contra SQL Server real, de
  componentes e end-to-end em desktop e telemóvel
- Um analisador de Roslyn que falha a compilação, e dois guardas que leem o código-fonte e falham a
  suite de testes
- Mais de 40 permissões, distribuídas por seis perfis

## 8. Estado

O âmbito planeado está concluído: gestão documental, motor de workflow com desenhador visual,
inteligência documental, pesquisa, caixa de entrada e integrações, portal externo, assinatura
eletrónica, relatórios e administração, mais o trabalho de acessibilidade, desempenho e
documentação.

Nada foi dado por terminado sem estar a funcionar, testado e documentado. O que o projeto não faz,
e porquê, está na secção de limitações conhecidas do README.

## 9. O que este projeto demonstra

Que sei construir um sistema empresarial completo — não uma funcionalidade isolada — com as
decisões difíceis tomadas conscientemente e escritas: segurança imposta no servidor,
isolamento de dados entre clientes, testes que provam comportamento em vez de confirmarem
implementação, e honestidade sobre o que o software é e o que não é.

E que, quando um erro aparece, a correção vai à origem: três dos defeitos encontrados deixaram
de ser possíveis, porque passaram a falhar a suite de testes.
