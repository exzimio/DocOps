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
