# ADR-004 — Abstração de fornecedor de IA com modo de demonstração determinístico

- **Estado:** aceite (implementação na Fase 5)
- **Data:** 2026-09-18
- **Âmbito:** classificação documental, extração de metadados, perguntas sobre documentos, pesquisa
  em linguagem natural

## Contexto

A IA é central no produto (classificar faturas, extrair fornecedor, número, datas, IVA e total,
responder a perguntas sobre um documento), mas não pode ser um requisito para executar, demonstrar ou
testar o projeto. Um avaliador tem de conseguir ver a funcionalidade sem chave de API, sem custo e
com resultados repetíveis.

## Decisão

Definir abstrações na camada de aplicação e fornecer duas implementações:

```csharp
public interface IAiDocumentService
{
    Task<ClassificationResult> ClassifyAsync(DocumentContent content, CancellationToken ct = default);
    Task<ExtractionResult> ExtractAsync(DocumentContent content, DocumentTypeSchema schema, CancellationToken ct = default);
    Task<DocumentAnswer> AnswerAsync(DocumentContent content, string question, CancellationToken ct = default);
}
```

1. **`DemoAiProvider`** — determinístico, baseado em regras e no conteúdo estruturado semeado.
   Identificado na interface e na resposta da API como modo de demonstração.
2. **`OpenAiCompatibleProvider`** — ativado por configuração (`Ai:Provider`, `Ai:Endpoint`,
   `Ai:ApiKey`, todos por variável de ambiente), compatível com Azure OpenAI.

## Regras não negociáveis

- **Nunca apresentar uma resposta de demonstração como tendo sido gerada por um modelo real.** A
  origem (`demo` ou `model`) faz parte do contrato da API e é visível na interface.
- **Nunca inventar citações.** Uma resposta a uma pergunta sobre um documento só cita a secção, a
  página ou o campo que existe no conteúdo processado.
- **A IA sugere, a pessoa decide.** Nenhum valor extraído é gravado sem aceitação explícita, campo a
  campo ou em bloco.
- **Cada aceitação e cada rejeição é auditada**, com o valor sugerido, o valor final e o indicador de
  confiança. É esse registo que permite medir a taxa de aceitação apresentada nos relatórios.
- **A autorização aplica-se antes da IA.** A pesquisa em linguagem natural constrói uma consulta que
  corre sob os mesmos filtros de organização e permissão de qualquer outra consulta: o modelo nunca
  vê, nem pode devolver, documentos que o utilizador não pode ver.

## Alternativas consideradas

**Depender apenas de um fornecedor real.** Rejeitado. Tornaria impossível demonstrar ou testar o
sistema sem credenciais e custos, e introduziria não-determinismo nos testes.

**Simular respostas no frontend.** Rejeitado categoricamente: seria uma maquete, não uma
funcionalidade, e violaria o princípio de que a interface não é fonte de verdade.

**Um único fornecedor abstraído por uma só interface genérica.** Rejeitado. Classificação, extração e
resposta a perguntas têm contratos, custos e modos de falha distintos; interfaces separadas mantêm as
implementações honestas.

## Consequências

- O projeto corre e demonstra-se sem qualquer conta de IA.
- Os testes são determinísticos.
- Trocar de fornecedor é uma alteração de configuração e de uma implementação, não do domínio.
- O modo de demonstração é limitado por construção: reconhece os documentos semeados e os formatos
  previstos, e é isso que declara.

## Como ficou, na prática

- **A interface chama-se `IAiDocumentService`** e tem três operações separadas, como previsto. A
  classificação recebe os tipos candidatos do catálogo da própria organização, em vez de os ter
  fixados no código: um tipo criado por um administrador é reconhecível pelos seus próprios rótulos.
- **O fornecedor de demonstração é uma tabela de regras, não um modelo.** Lê o texto real do
  documento — os exemplos em `samples/` têm a disposição de faturas e contratos verdadeiros, não os
  metadados escritos ao contrário — e devolve, para cada valor, a linha de onde o leu. O mesmo
  documento dá sempre o mesmo resultado.
- **«Nunca inventar citações» virou código.** `DocumentText` numera as linhas e verifica cada
  citação; o `AiDocumentAnalyzer` aplica a guarda a qualquer fornecedor. Uma resposta cujas citações
  caem todas é retida, e o número de citações descartadas vai na resposta.
- **Ao modelo exige-se mais do que às regras**, não menos: pede-se-lhe as palavras exatas de onde
  leu cada valor, e o valor só sobrevive se essas palavras estiverem no documento **e** o valor se
  puder reler a partir delas. Uma citação verdadeira ao lado de um número diferente é descartada.
- **A revisão é a unidade de trabalho.** Uma análise (`AiProcessingRun`) guarda o que foi sugerido e
  o que cada pessoa decidiu sobre cada campo. Aplicar escreve numa só transação, pelo mesmo
  componente que a edição manual usa, e é recusado enquanto a classificação sugerida não for
  explicitamente aceite ou rejeitada: mudar o tipo de um documento nunca é o comportamento por
  omissão.
- **Dentro de um processo, a IA não é chamada pelo motor.** O passo põe a análise em fila e o worker
  processa-a fora da transação. O passo escreve variáveis `ai.*` para uma condição poder decidir,
  mas nunca aplica nada — as sugestões esperam por uma pessoa, como em qualquer outra análise.
- **Perguntas sobre aprovações não são respondidas pelo texto.** «Quem aprovou este documento?» é
  respondida a partir das tarefas de aprovação concluídas, citando esse registo. É a resposta
  correta e é verificável; procurá-la no texto do documento seria inventá-la.
