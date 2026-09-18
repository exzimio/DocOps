# Modelo de segurança

Este documento descreve os controlos implementados, os que estão planeados e as suposições em que
assentam. É um projeto de demonstração: não foi submetido a auditoria de segurança independente nem
se destina a tratar dados reais.

## 1. Princípios

1. **Nunca confiar no cliente.** Identificador de organização, identificador de utilizador e
   permissões vêm da sessão do servidor, nunca do pedido.
2. **Defesa em profundidade.** Cada garantia importante tem mais do que um ponto de aplicação.
3. **Falhar de forma fechada.** Na dúvida, negar e registar.
4. **Provar o que aconteceu.** Ações relevantes produzem registo de auditoria imutável.

## 2. Autenticação

| Controlo | Implementação |
| --- | --- |
| Mecanismo | ASP.NET Core Identity com cookie de sessão |
| Cookie | `docops.session`, `HttpOnly`, `SameSite=Lax`, `Secure` conforme o esquema do pedido |
| Armazenamento no cliente | Nenhum. Não há *token* acessível a JavaScript (sem `localStorage`, sem `sessionStorage`) |
| Palavra-passe | Mínimo 12 caracteres, maiúscula, minúscula, dígito e símbolo; hash PBKDF2 do Identity |
| Bloqueio | 5 tentativas falhadas → 15 minutos de bloqueio, aplicável também a contas novas |
| Enumeração de contas | Palavra-passe errada e conta inexistente devolvem exatamente a mesma resposta |
| Revalidação | *Security stamp* revalidado a cada 5 minutos: permissões revogadas deixam de funcionar sem esperar pelo fim da sessão |
| Sessão | 8 horas com renovação deslizante |

Em produção, o cookie deve ser servido apenas sobre HTTPS (`CookieSecurePolicy.Always`) e a
aplicação deve estar atrás de HSTS.

## 3. Autorização

- Os *endpoints* exigem **permissões**, não perfis: `[HasPermission(Permissions.DocumentsApprove)]`.
- As permissões são *claims* de perfil guardadas em `AspNetRoleClaims` — administráveis sem novo
  *deploy* e auditáveis como qualquer outra alteração.
- O `PermissionPolicyProvider` gera políticas a pedido; acrescentar uma permissão não exige alterar
  o arranque da aplicação.
- O frontend esconde o que o utilizador não pode usar, mas essa decisão **não é** um controlo de
  segurança: a mesma regra é aplicada no servidor em todos os casos.
- A autorização por recurso (documento) combina propriedade, permissões explícitas a utilizador,
  departamento ou perfil, e a classificação de sensibilidade. O resultado é intersetado com as
  permissões do perfil na organização: partilhar um documento com um colaborador nunca o transforma
  em aprovador. O painel de *acesso efetivo* mostra a razão de cada regra aplicada.
- Utilizadores externos (fornecedores, parceiros) nunca obtêm acesso por classificação: só por
  permissão explícita.
- A lista de documentos aplica as regras de acesso **dentro da consulta SQL**, para que a contagem
  total e a paginação não revelem a existência de documentos que o utilizador não pode ver.
- As tarefas seguem a mesma lógica: uma tarefa é visível a quem a tem atribuída e a quem pertence ao
  departamento ou perfil a que foi oferecida. Agir sobre ela exige `tasks.act`, e reatribuir exige
  `tasks.reassign` — ver uma tarefa nunca basta para a decidir.
- As ações disponíveis numa tarefa são calculadas no servidor e enviadas com ela; a interface
  apresenta o que lhe é dito, e cada *endpoint* volta a verificar a mesma regra antes de agir.

## 4. Isolamento entre organizações

| Camada | Controlo |
| --- | --- |
| Sessão | O identificador da organização existe apenas como *claim*; nenhuma rota o aceita como parâmetro |
| Leitura | Filtros globais de consulta em todas as entidades com organização |
| Escrita | `TenantGuardInterceptor` atribui a organização a novas linhas e recusa escritas cruzadas |
| Testes | Testes de integração verificam ambos os sentidos com duas organizações reais em base de dados |

## 5. Proteção contra CSRF

O cookie é `SameSite=Lax`, o que impede o browser de o enviar em pedidos `POST` originados noutro
sítio. Adicionalmente, `CookieOriginValidationMiddleware` recusa qualquer método que altere estado
(`POST`, `PUT`, `PATCH`, `DELETE`) cujo `Origin` — ou, na ausência deste, o `Referer` — não seja a
própria aplicação ou uma origem explicitamente autorizada. A resposta é `403` com
`code: origin_not_allowed` e fica registada.

Esta abordagem protege toda a superfície da API sem exigir gestão de *tokens* antifalsificação no
cliente.

## 6. Cabeçalhos de segurança

| Cabeçalho | Valor |
| --- | --- |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` |
| `Cross-Origin-Resource-Policy` | `same-site` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` |

A API serve exclusivamente JSON, pelo que a CSP pode ser máxima. A CSP da aplicação web é definida
no alojamento estático do frontend.

## 7. Limitação de tráfego

| Âmbito | Limite |
| --- | --- |
| Global | 300 pedidos por minuto por utilizador autenticado ou endereço IP |
| Autenticação | 10 tentativas por 5 minutos por endereço IP |

Excedido o limite, a resposta é `429`. A limitação por IP complementa — não substitui — o bloqueio de
conta do Identity, que é por conta e, por isso, imune à partilha de endereço.

Os limites são configuráveis (secção `RateLimiting`) porque num ambiente com endereço partilhado —
escritório atrás de NAT, *runner* de CI, máquina local a correr toda a bateria de testes — um limite
por IP penaliza utilizadores legítimos. O ambiente de desenvolvimento eleva o limite de autenticação
para 100; produção usa as predefinições.

## 8. Validação de entrada

- Validação com FluentValidation aplicada por um filtro global: nenhum controlador pode esquecer-se
  de validar.
- Erros devolvem `400` em `ProblemDetails` com `code: validation_failed` e os erros por campo.
- O comprimento da palavra-passe é limitado antes do cálculo de *hash*, o que evita trabalho
  criptográfico desnecessário com cargas grandes.
- Carregamento de ficheiros: lista branca de extensões e de tipos de conteúdo, dimensão máxima
  configurável, nome de ficheiro higienizado e chave de armazenamento gerada pelo servidor — um nome
  de ficheiro manipulado não consegue escapar da raiz de armazenamento, e o caminho resolvido é
  validado contra essa raiz antes de qualquer leitura ou escrita.

## 9. Auditoria

- Registo imutável em `AuditEvents`: a aplicação lança exceção em qualquer tentativa de alteração ou
  eliminação.
- O ator é determinado pelo servidor, não pelo chamador.
- Escrita em contexto próprio: o registo sobrevive ao *rollback* da operação que descreve.
- Campos com `password`, `secret`, `token`, `apikey` ou `connectionstring` no nome são redigidos
  antes de serem serializados.

Em produção, complemente com uma restrição ao nível da base de dados:

```sql
DENY UPDATE, DELETE ON dbo.AuditEvents TO [docops_app];
```

## 10. Registos e dados sensíveis

- Os logs nunca contêm palavras-passe, cookies nem cadeias de ligação.
- Os *health checks* devolvem apenas nome, estado e duração de cada verificação; nenhuma mensagem de
  exceção, nome de servidor ou cadeia de ligação é exposta.
- As respostas de erro nunca incluem *stack traces*: o cliente recebe um identificador de correlação
  que permite localizar o detalhe nos logs do servidor.

## 11. Gestão de segredos

- O repositório não contém segredos de produção.
- A única credencial versionada é a do container SQL Server de desenvolvimento, ligado apenas a
  `127.0.0.1` e documentada como tal.
- Em desenvolvimento, use *user secrets* ou `.env` (ambos ignorados pelo Git); em produção, use as
  variáveis de ambiente da plataforma ou um cofre de segredos.

## 12. Ameaças consideradas

| Ameaça | Mitigação |
| --- | --- |
| Roubo de sessão por XSS | Cookie `HttpOnly`; nenhum *token* acessível a JavaScript |
| CSRF | `SameSite=Lax` + validação de origem em métodos que alteram estado |
| Força bruta e *credential stuffing* | Bloqueio de conta + limitação por IP + respostas indistinguíveis |
| Enumeração de contas | Resposta idêntica para conta inexistente e palavra-passe errada |
| Fuga entre organizações | Filtros de consulta + intercetor de escrita + testes dedicados |
| Acesso indevido a documento | Avaliação por recurso no servidor, com teto pelas permissões do perfil |
| Travessia de caminho no armazenamento | Chave gerada pelo servidor e caminho validado contra a raiz |
| Carregamento de ficheiro perigoso | Lista branca de extensões e tipos, limite de dimensão, nome higienizado |
| Decisão de aprovação por quem não devia | Autorização por tarefa no servidor, verificada em cada ação |
| Execução de código a partir de dados guardados | As condições de workflow são um operador de conjunto fechado sobre uma variável, não uma linguagem de expressões |
| Processo a contornar as regras do documento | As alterações de estado configuradas num nó passam pela máquina de estados do documento |
| Escalada de privilégios | Autorização por permissão no servidor; *claims* do cookie assinadas e revalidadas |
| Injeção SQL | EF Core com consultas parametrizadas; sem concatenação de SQL |
| Adulteração de auditoria | Intercetor de imutabilidade + revogação de permissões na base de dados |
| Divulgação em mensagens de erro | `ProblemDetails` uniformes, sem detalhe interno |

## 13. Trabalho em segundo plano

O worker corre sem organização no contexto ambiente, o que lhe permite processar todas as
organizações. É uma exceção deliberada: o processo não expõe superfície HTTP, os seus trabalhos são
apenas escalonamento de prazos e retoma de temporizadores, e cada escrita continua a passar pelos
mesmos intercetores de auditoria e de isolamento.

## 14. Limitações conhecidas

- Sem autenticação multifator (fora do âmbito desta demonstração; a arquitetura de Identity suporta a
  adição).
- Sem rotação automática das chaves de proteção de dados em ambiente distribuído — em produção seria
  necessário um repositório de chaves partilhado.
- A assinatura eletrónica (Fase 7) é uma demonstração funcional e **não tem qualquer validade legal**.
- O fornecedor de IA de demonstração é determinístico e está identificado como tal: nenhuma resposta
  de demonstração é apresentada como tendo sido gerada por um modelo real.
