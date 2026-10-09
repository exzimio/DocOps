# ADR-010: Cada indicador é uma consulta, e a ausência de base não é zero

- **Estado:** aceite
- **Data:** 2026-09-28
- **Âmbito:** painel de análises, exportação de relatórios

## Contexto

O requisito é invulgarmente direto: todos os indicadores vêm dos dados reais da base de dados, e
nenhum número do painel é escrito à parte dos dados semeados.

A insistência não é gratuita. Um painel é o sítio do produto onde é mais fácil mentir sem que
ninguém repare: um número escrito à mão tem exatamente o mesmo aspeto de um número calculado, e
quem o lê não tem como distinguir. Todas as outras regras deste projeto (nada de fachada, nenhum
botão sem ação) encontram aqui a sua versão mais difícil de fiscalizar.

Havia três decisões a tomar, e as três têm alternativas defensáveis.

## Decisão 1: calcular no momento, sem agregados nem cache

Cada indicador do painel é uma consulta executada no pedido, sobre as mesmas tabelas com que a
aplicação trabalha. Não existe tabela de agregados, nem cache, nem processo a manter totais.

**O que isto custa:** latência. Abrir o painel executa uma dúzia de consultas agregadas, e num
catálogo grande isso nota-se. É um custo real e assumido.

**A alternativa rejeitada** era manter contadores atualizados por um processo em segundo plano, ou
uma cache com invalidação. É mais rápida e é o que um sistema com volume acabaria por fazer. Traz,
porém, a única avaria que este produto não pode ter: **o painel a discordar da lista que está ao
lado dele**. Um contador que perdeu uma invalidação mostra dezassete onde a lista mostra dezasseis,
e a partir daí nenhum número do painel volta a merecer confiança, nem os que estão certos.

Enquanto o desempenho for aceitável, a consulta directa é a escolha honesta. Quando deixar de ser,
a solução é indexação e consultas melhores antes de agregados mantidos à parte; e se um dia forem
inevitáveis, a regra é que o painel diga de quando são, em vez de fingir que são de agora.

## Decisão 2: os números são a fatia de quem pergunta

`ReportingService` reutiliza `DocumentAccessResolver.BuildVisibilityPredicate`, o mesmo predicado
traduzível para SQL que a listagem, a pesquisa e o portal usam. Não existe um caminho «de relatório»
com regras próprias.

**A alternativa rejeitada** era calcular o painel ao nível da organização, porque «são só totais, não
revelam conteúdo». Não é verdade: uma contagem é informação. Saber que existem catorze documentos
confidenciais no departamento jurídico, e que quatro entraram esta semana, é saber alguma coisa
sobre eles. As estatísticas de IA seguem a mesma regra, pela mesma razão.

**A consequência que se aceita:** duas pessoas da mesma organização veem painéis diferentes. É o
correcto, e a interface diz-lhe porquê em vez de deixar a diferença por explicar.

## Decisão 3: uma medida sem base devolve `null`, e o ecrã di-lo por extenso

`MetricDto.Value` é `decimal?`. Quando não há denominador (nenhum processo concluído no período,
nenhuma extração de IA para aceitar ou rejeitar), a resposta é `null`, e a interface escreve «sem
base de cálculo no período».

**A alternativa rejeitada** era devolver zero, que é o que quase todos os painéis fazem. Zero é uma
afirmação: mediu-se e deu zero. Numa janela em que nada aconteceu, «0% de automação» lê-se como um
mau resultado quando na verdade não houve resultado nenhum: é uma mentira com aspeto de facto, e
das piores, porque é plausível.

Nenhum teste de contrato apanha este erro: o tipo é `decimal`, o valor é zero, e zero é um
`decimal` perfeitamente válido. Por isso a regra tem um teste de ponta a ponta próprio, que abre o
painel numa janela de sete dias e exige «Sem base de cálculo no período» em vez de um zero.

## Consequências

- Os testes do painel **não podem afirmar contagens fixas**: os números mudam com o que as outras
  suites criam. Afirmam relações (que uma janela mais curta devolve um painel diferente, que uma
  medida sem base diz que não tem base), e a guarda em `TestSuiteConventionTests` falha a suite de
  testes se alguém escrever uma contagem absoluta.
- Gráficos com medidas de escalas diferentes ficam em gráficos separados. Um eixo duplo faria
  parecer comparável o que não é, e o problema não é estético.
- A exportação para CSV reconverte os estados para nomes legíveis: um ficheiro não tem quem o
  traduza do lado de lá.
