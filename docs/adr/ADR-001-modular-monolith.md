# ADR-001: Monólito modular em vez de microserviços

- **Estado:** aceite
- **Data:** 2026-09-18
- **Âmbito:** estrutura global da solução

## Contexto

A plataforma cobre dezasseis áreas de domínio (documentos, workflows, tarefas, IA, pesquisa,
integrações, assinatura, auditoria, relatórios, administração, entre outras). As operações centrais
atravessam várias áreas numa única unidade de trabalho: aprovar uma fatura conclui uma tarefa, faz
avançar uma instância de workflow, altera o estado do documento e escreve auditoria.

## Decisão

Construir um **monólito modular**: um processo, uma base de dados, fronteiras de módulo explícitas no
código (`Domain`, `Application`, `Infrastructure`, `Api`) e comunicação por interfaces.

## Alternativas consideradas

**Microserviços por área de domínio.** Rejeitado. As operações nucleares são transacionais e
atravessam módulos; distribuí-las obrigaria a sagas e a consistência eventual para resolver um
problema que uma transação de base de dados resolve. O custo operacional (descoberta de serviços,
rasto distribuído, versionamento de contratos, ambientes de desenvolvimento) não é compensado por
nenhum requisito real de escala ou de autonomia de equipa.

**Camadas sem módulos.** Rejeitado. Sem fronteiras explícitas, o acoplamento cresce silenciosamente e
qualquer extração futura torna-se inviável.

**CQRS com modelos de leitura e escrita separados.** Rejeitado. Os padrões de leitura ainda
não justificam a duplicação de modelos. Projeções direcionadas (`Select` para DTO) resolvem hoje o
problema de desempenho que o CQRS resolveria.

## Consequências

**Positivas**
- Uma transação garante consistência entre módulos; sem compensações nem *saga orchestration*.
- Um clone, um `docker compose up` e dois comandos põem tudo a correr.
- Refatorações entre módulos são verificadas pelo compilador.

**Negativas**
- Escala em conjunto: não é possível dimensionar só o módulo de IA.
- A disciplina das fronteiras depende de revisão: o compilador impede dependências entre projetos,
  mas não impede uma dependência indevida dentro do mesmo projeto.

**Mitigação:** cada módulo comunica por interfaces definidas em `Application`. Se, no futuro, um
módulo justificar isolamento (tipicamente o processamento de IA, por perfil de carga distinto), a
interface já existe e a extração afeta a implementação, não os chamadores.

## Sinais de revisão

Reavaliar se: o processamento de IA passar a dominar o consumo de recursos; uma área exigir um ciclo
de *deploy* independente; ou o tempo de arranque/execução dos testes deixar de ser aceitável.
