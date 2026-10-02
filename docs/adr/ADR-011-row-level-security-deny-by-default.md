# ADR-011 — A base de dados nega por omissão, e a dispensa é uma identidade

- **Estado:** aceite (implementação em manutenção, depois da Fase 10)
- **Data:** 2026-10-02
- **Âmbito:** isolamento entre organizações, identidades de base de dados, implantação

## Contexto

O isolamento entre organizações vivia em duas camadas. A primeira são os filtros globais de consulta
do EF Core. A segunda, acrescentada na Fase 9, é uma política de segurança ao nível da linha do SQL
Server sobre as 26 tabelas que têm `TenantId`.

A segunda camada existe porque a primeira já falhou. O defeito 52 foi uma fuga verdadeira:
`IgnoreQueryFilters()` dentro de uma projeção desliga os filtros da consulta inteira, e um
administrador de uma organização viu as etiquetas de outra.

**O predicado da Fase 9 deixava passar quando não havia organização no contexto da sessão.** Era
deliberado — o worker corre acima de todas as organizações, e as migrações e a semeadura não
pertencem a nenhuma — e estava escrito como limitação assumida. O que não estava escrito com exatidão
era a consequência, e durante algum tempo a documentação deste projeto afirmou o contrário: **uma
ferramenta ligada diretamente à base nunca escreve contexto nenhum, logo via todas as organizações**.
A segunda parede só protegia quem já estava protegido pela primeira.

Foi uma revisão técnica externa que apanhou a contradição, lendo uma frase deste repositório contra
outra.

## Decisão 1 — O predicado nega por omissão

```sql
WHERE @TenantId = CAST(SESSION_CONTEXT(N'docops.tenant') AS uniqueidentifier)
   OR (CAST(SESSION_CONTEXT(N'docops.system') AS bit) = 1
       AND USER_NAME() = N'app_system');
```

Uma ligação que não declara nada vê zero linhas. O que antes acontecia por ausência de contexto passa
a ser uma declaração explícita, feita no código, num sítio só.

**A alternativa rejeitada** era manter o comportamento e documentar melhor a limitação. Documentar
melhor uma porta aberta não a fecha, e o caso que ela deixava passar é o realista: não é o atacante
com a credencial, é a ligação inocente — um cliente de SQL, uma ferramenta de relatórios, um guião de
migração de dados — que vê tudo sem querer e sem ninguém perceber.

**O que isto custa:** um modo de falhar novo, e pior do que não ter política. Código que **julga** ter
dispensa e não tem lê zero linhas, sem erro. Aconteceu durante a implementação: a semeadura correu num
âmbito normal e o ASP.NET Core Identity leu a ausência de linhas como conflito de concorrência, uma
mensagem que não aponta para causa nenhuma. É por isso que a cadeia de sistema é obrigatória e que o
âmbito de sistema se eleva antes de qualquer ligação abrir.

## Decisão 2 — A dispensa é uma chave **mais** uma identidade

Declarar `docops.system` no contexto da sessão não basta: a ligação também tem de se fazer com a
identidade `app_system`. Quem conhecer o nome da chave e a escrever, ligado com outra identidade,
continua sem ver nada.

Isto obrigou a três identidades de base de dados, onde havia uma:

| Identidade | Quem a usa | O que não pode |
| --- | --- | --- |
| `app_tenant` | Todos os pedidos normais | Alterar o esquema. Obter dispensa |
| `app_system` | Autenticação, worker, semeadura | Alterar o esquema |
| `docops_migrator` | Só o passo de migração | Atender pedidos: a aplicação não tem esta cadeia |

A separação entre as duas primeiras é o que torna a dispensa inalcançável a partir de um pedido: o
código pode pedir um âmbito de sistema, mas esse âmbito liga-se por outra cadeia, e é a identidade da
ligação que a base verifica. A terceira é o que permite às outras duas **não** poderem alterar o
esquema, porque quem pode alterar o esquema também pode largar a política.

**A alternativa rejeitada era a pertença a um perfil** (`IS_ROLEMEMBER`), e a razão por que foi
rejeitada não é a que se previa. A discussão que originou este trabalho avisava que um membro de
`sysadmin` passaria implicitamente numa verificação de perfil, o que tornaria a abordagem inútil.
Medido: `IS_ROLEMEMBER` devolve `0` para `sa` num perfil de que ele não é membro. **A suposição estava
errada, e qualquer das duas formas serviria.** Ficou a comparação direta a `USER_NAME()` por ser mais
fácil de verificar ao ler o predicado: não depende de quem foi acrescentado a que perfil ao longo do
tempo.

Medido também, em SQL cru, contra a base depois de aplicada a migração:

| Quem se liga | O que declara | Vê |
| --- | --- | --- |
| `sa` | Nada | **0 linhas** |
| `sa` | Escreve a chave da dispensa à mão | **0 linhas** |
| `app_tenant` | Nada | **0 linhas** |
| `app_system` | O modo de sistema | todas |

`sa` mapeia para `dbo` em qualquer base de dados, e por isso `USER_NAME()` nunca é `app_system`.

## Decisão 3 — Predicados de bloqueio na escrita, com a mesma função

Cada tabela tem três predicados de bloqueio — `AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE` — e a
função que decide quem lê é a mesma que decide quem escreve. Não são duas regras a manter
sincronizadas: é uma.

O caso que isto fecha, e que o filtro sozinho não alcançava, é **mover** uma linha para outra
organização. A linha de partida é visível a quem a move, e o resultado é uma linha coerente, na
organização errada, sem nada no registo que o denuncie.

Não há `BEFORE DELETE`: o predicado de filtro já torna invisível o que não pertence à organização, e
o que não se vê não se apaga.

**A alternativa rejeitada** era confiar no `TenantGuardInterceptor`, que já recusa escritas cruzadas.
É código da aplicação, portanto a mesma classe de defesa que falhou no defeito 52, e SQL escrito à mão
não passa por ele.

## Decisão 4 — A política é largada e recriada, não alterada

As migrações desta decisão largam a política, recriam a função e recriam a política. O `SCHEMABINDING`
que torna o predicado barato — entra no plano da consulta em vez de ser chamado linha a linha — também
o prende: o motor recusa alterar a função enquanto a política a referenciar.

O custo é repetir a lista das 26 tabelas em cada migração. O ganho é o desempenho do predicado, e a
lista está coberta por um teste que a lê da própria base de dados.

## Consequências

- **As três identidades não são opcionais.** A aplicação recusa arrancar sem a cadeia de sistema, e o
  passo de migração recusa sem a do migrador. Uma pilha ligada como `sa` arrancaria saudável e veria
  zero linhas, o que é pior do que não arrancar.
- **A base de dados tem de ser criada por quem não é a aplicação.** `docops_migrator` molda o esquema
  mas não cria bases, e os utilizadores de base de dados vivem dentro dela: largar a base leva-os com
  ela. O `scripts/create-database-identities.sql` faz as duas coisas e é repetível, e o
  `scripts/reset-demo-db.ps1` corre-o a cada reposição — foi a falta disso que travou a suíte
  end-to-end.
- **A imutabilidade da auditoria deixou de ser conselho.** Estava no `SECURITY.md` desde a Fase 2 como
  recomendação para produção, com um `DENY` sobre um login que nunca existiu. Com identidades próprias
  há a quem recusar, e a recusa está na migração.
- **Uma migração que mexa em dados é filtrada como qualquer outra ligação.** O contexto usado para
  migrar não leva interceptores, porque uma migração não pertence a organização nenhuma: uma migração
  de dados sobre uma tabela com `TenantId` vê zero linhas, em silêncio. Está escrito em
  `DatabaseMigrator` para quem escrever a próxima.
- **Os testes de integração criam as três identidades no seu contentor.** Com `sa` em todo o lado,
  passariam com ou sem política: provariam o filtro do EF Core e mais nada.
- **`sa` continua a poder largar a política.** Não há predicado que o impeça, e não é trabalho de um
  predicado: fecha-se não dando essa credencial a ninguém. Fica escrito como limite assumido em vez de
  ficar implícito.
