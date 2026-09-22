# ADR-005 — Pesquisa em SQL Server, sem motor de pesquisa dedicado

- **Estado:** aceite (implementação na Fase 6)
- **Data:** 2026-09-18
- **Âmbito:** pesquisa por metadados, texto integral e linguagem natural

## Contexto

A pesquisa tem de combinar filtros estruturados (tipo, estado, departamento, responsável, intervalo
de datas, etiquetas, estado de workflow) com texto livre sobre o conteúdo dos documentos, respeitando
sempre as permissões do utilizador e a organização a que pertence.

## Decisão

Usar **SQL Server** como motor de pesquisa:

1. **Filtros de metadados** — consultas LINQ sobre colunas indexadas, com paginação no servidor e
   projeções para DTO.
2. **Texto integral** — índice *full-text* do SQL Server sobre o conteúdo extraído dos documentos,
   consultado com `CONTAINS`/`FREETEXT`.
3. **Linguagem natural** — `IAiSearchService` traduz a pergunta do utilizador num conjunto de filtros
   estruturados (tipo de documento, estado, intervalo de datas, departamento) que é depois executado
   como qualquer outra consulta.

A autorização aplica-se **na consulta**, não ao resultado: o predicado de permissões e o filtro de
organização fazem parte do SQL, pelo que a contagem total e a paginação são corretas — e não existe o
problema clássico de filtrar a página depois de a obter.

## Alternativas consideradas

**Elasticsearch ou OpenSearch.** Rejeitado nesta escala. Acrescentaria um segundo sistema de
persistência, sincronização de índices, um novo modo de falha e consistência eventual entre o que
está na base de dados e o que está no índice — para volumes que o SQL Server trata sem dificuldade.
Pior ainda, a filtragem por permissões num motor externo obriga a replicar o modelo de autorização,
que é exatamente onde erros de segurança nascem.

**Azure AI Search.** Rejeitado como dependência: exigiria conta de nuvem para desenvolver e testar.
Permanece uma opção futura atrás da mesma interface `ISearchService`.

**Só `LIKE '%termo%'`.** Rejeitado. Não usa índice, não ordena por relevância e degrada-se
rapidamente com o volume.

## Consequências

**Positivas**
- Um único sistema de persistência: sem sincronização nem divergência entre base de dados e índice.
- As regras de autorização existem uma só vez, no sítio onde os dados vivem.
- Transacionalmente correto: um documento aprovado é imediatamente pesquisável nesse estado.

**Negativas**
- Menos capacidades de relevância e agregação do que um motor dedicado (sem facetas avançadas nem
  *did-you-mean*).
- O desempenho do texto integral depende do dimensionamento da instância de SQL Server.

**Sinais de revisão:** volume acima de alguns milhões de documentos, necessidade de facetas ricas,
pesquisa semântica por *embeddings* ou exigência de latência abaixo de 50 ms em texto integral. Nessa
altura, a interface `ISearchService` permite introduzir um motor externo para a componente de texto,
mantendo a filtragem de permissões na base de dados.

---

## Como ficou, na prática (Fase 6)

A decisão manteve-se; três pormenores da implementação merecem registo, porque nenhum deles era
óbvio a partir da decisão.

### O índice de texto integral não vem na imagem oficial do SQL Server

A imagem `mcr.microsoft.com/mssql/server:2022-latest` não traz o componente de texto integral, e o
pacote que o instala vive num repositório que a imagem também não configura. Sem ele, `CONTAINS`
falha — não devolve zero resultados, falha.

O projeto passou a **construir a sua própria imagem** ([`docker/sql-server/Dockerfile`](../../docker/sql-server/Dockerfile)),
que acrescenta o repositório e instala `mssql-server-fts` na mesma versão do servidor já presente. O
`docker-compose.yml` constrói essa imagem em vez de puxar a oficial.

Isto resolve o ambiente de desenvolvimento e de demonstração, mas **não** o dos testes de
integração: o Testcontainers levanta a imagem oficial. Daí as duas consequências seguintes.

### A pesquisa deteta o que o servidor sabe fazer e diz qual dos dois caminhos correu

`FullTextAvailability` pergunta uma vez ao servidor se o componente está instalado *e* se os índices
que a migração cria existem, e guarda a resposta. A partir daí:

- **Com índice** — `CONTAINS` sobre `Documents(Title, Reference, Summary)`,
  `DocumentVersions(ExtractedText)` e `DocumentMetadataValues(TextValue)`.
- **Sem índice** — as mesmas colunas, comparadas por subcadeia. Pergunta mais estreita, sem
  radicais de palavra e sem ordenação por relevância.

Cada resposta da API transporta `diagnostics.fullTextIndexUsed` e `fullTextAvailable`, e a interface
mostra-o por extenso ao fundo da página. Uma pesquisa que se degradou em silêncio pareceria uma
pesquisa que encontrou menos — e isso seria mentir sobre o que o sistema fez.

A migração que cria o catálogo e os índices é escrita à mão, corre fora de transação (o SQL Server
recusa criar um catálogo de texto integral dentro de uma) e está guardada por
`SERVERPROPERTY('IsFullTextInstalled')`. Num servidor sem o componente, a migração é aplicada sem
criar nada e a aplicação arranca na mesma.

### `CONTAINSTABLE` e o `RANK` do SQL Server não foram usados

A relevância podia vir do `RANK` de `CONTAINSTABLE`. Não veio, por duas razões:

1. **Não compõe com a consulta de autorização.** `CONTAINSTABLE` é uma tabela, não um predicado;
   juntá-la obrigaria a materializar o conjunto de chaves antes de aplicar o predicado de permissões
   — exatamente o «filtrar a página depois de a obter» que esta ADR rejeita.
2. **O `RANK` não é comparável entre consultas** e a própria documentação da Microsoft diz que não é
   uma percentagem. Mostrá-lo como tal seria inventar um número.

Em vez disso, a ordenação usa **pesos por sítio onde o termo foi encontrado** — título 5, referência
4, metadados 3, resumo 2, etiqueta 2, corpo 1 — somados numa expressão `CASE` que o SQL Server
ordena e pagina. São fixos, estão escritos em `SearchService` e a interface explica-os ao passar o
rato sobre a percentagem. Uma ordenação simples que se consegue justificar vale mais do que uma
sofisticada que ninguém consegue explicar.

### As etiquetas são comparadas por subcadeia, nos dois caminhos

`Tags` tem filtro global de organização, o que leva o EF Core a juntá-la como tabela derivada — e
`CONTAINS` só lê colunas de tabelas reais. Como uma etiqueta é um rótulo curto, a comparação por
subcadeia é de qualquer modo a pergunta certa a fazer-lhe; o índice sobre `Tags(Name)` foi retirado
da migração por já não servir ninguém.

### A leitura de perguntas nunca toca nos dados

`IAiSearchService` devolve **filtros**, não resultados. Quem executa é o `ISearchService`, com a
mesma autorização de qualquer outra pesquisa. O leitor de demonstração é uma tabela de expressões
(`SearchVocabulary`) mais uma consulta ao catálogo da própria organização; o leitor por modelo recebe
apenas a pergunta e os **nomes** do catálogo, e cada nome que devolve é reconferido contra esse
catálogo antes de virar filtro. Um leitor que alcançasse os dados poderia ser convencido a alcançar
dados que quem pergunta não pode ver.
