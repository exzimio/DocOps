# Guião de demonstração

Todas as organizações, pessoas e documentos são fictícios. Nenhuma parte desta demonstração está
associada a qualquer produto ou fornecedor comercial.

## Preparação (uma vez)

```bash
docker compose up -d                                  # aguarde "healthy"
dotnet run --project src/Api/DocOps.Api.csproj        # http://localhost:5180
npm --prefix src/Web run dev                          # http://localhost:5173
```

Palavra-passe de todas as contas: `Demo!Pass123`

---

## Demonstração disponível na Fase 1 — Identidade, acesso e isolamento (≈4 minutos)

### 1. Página pública (20 s)

Abrir <http://localhost:5173>. Referir a proposta de valor, as seis capacidades e a nota de projeto
independente. Alternar o idioma para inglês e o tema para escuro — a preferência persiste.

### 2. Autenticação real (40 s)

Em **Abrir demonstração interativa**:

- Submeter o formulário vazio: validação no cliente com mensagens em pt-PT.
- Entrar com `ricardo.lopes@asteria.example` e uma palavra-passe errada: resposta `401` com
  mensagem clara, sem revelar se a conta existe.
- Entrar com a palavra-passe correta.

Nas ferramentas de programador, mostrar o cookie `docops.session`: `HttpOnly`, `SameSite=Lax`.
Em `document.cookie` na consola, o cookie **não** aparece — nenhum *token* está acessível a
JavaScript.

### 3. Painel com dados reais (60 s)

O painel mostra o perfil, a organização, os departamentos e as permissões efetivas. Nada disto está
codificado na interface: tudo vem de `/api/auth/me` e `/api/tenants/current`. Na rede, mostrar as
duas respostas.

Reparar que Ricardo Lopes tem `documents.approve` (é aprovador financeiro) mas **não** tem
`admin.users`.

### 4. Isolamento entre organizações (60 s)

Terminar sessão e entrar com `admin@vertice.example`.

A mesma aplicação, o mesmo *endpoint* `/api/tenants/current`, resultado completamente diferente:
Vértice Industrial com dois departamentos. Não existe forma de pedir os dados da Asteria Group — a
organização vem da sessão e não há rota que a aceite como parâmetro.

### 5. Trilho de auditoria (40 s)

Cada tentativa de autenticação ficou registada:

```bash
docker exec docops-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Local_Dev_Passw0rd!' -C -d DocOpsControlTower -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT TOP 10 CONVERT(varchar(19),OccurredAtUtc,120)+' | '+Action+' | '+ActorDisplayName+' | '+ISNULL(Summary,'') FROM AuditEvents ORDER BY OccurredAtUtc DESC;"
```

Mostrar `user.login.failed` (severidade de segurança), `user.login.succeeded` e `user.logout`, cada um
com identificador de correlação. Tentar alterar uma dessas linhas pela aplicação resulta em exceção:
a auditoria é imutável por construção.

### 6. Superfície da API e saúde do sistema (40 s)

- <http://localhost:5180/scalar/v1> — documentação OpenAPI gerada.
- <http://localhost:5180/health> — estado das dependências, sem revelar infraestrutura.
- `curl -X POST http://localhost:5180/api/auth/login -H "Content-Type: application/json" -d '{"email":"a@b.com","password":"x"}'`
  devolve `403 origin_not_allowed`: um pedido sem origem autorizada é recusado, mesmo antes de
  qualquer verificação de credenciais.

---

## Demonstração disponível na Fase 2 — Repositório documental (≈5 minutos)

### 1. Lista de documentos (60 s)

Entre como `ricardo.lopes@asteria.example` e abra **Documentos**. São 37 documentos de demonstração
em 8 tipos, com estados, classificações, versões e etiquetas coerentes.

- Escreva `Northstar` na pesquisa: a lista reduz-se aos documentos desse fornecedor. A pesquisa corre
  no servidor, sobre título, referência, descrição e metadados.
- Clique no estado **Aguarda aprovação**: ficam as faturas e pedidos por decidir.
- Repare no rodapé: a contagem e a paginação vêm do servidor — o browser nunca recebe a lista toda.

### 2. Detalhe, metadados e versões (90 s)

Abra a fatura **Atlas Tecnologia**.

- **Visão geral** mostra as propriedades e os metadados tipados do tipo "Fatura de fornecedor"
  (fornecedor, número, datas, IVA, total, moeda). Os campos não estão codificados na interface: vêm
  do esquema de metadados definido no servidor.
- **Versões** mostra o histórico, com autor, comentário, dimensão e impressão digital SHA-256 de cada
  versão. Descarregue a versão 1 e a versão atual: são ficheiros diferentes, ambos preservados.
- Carregue uma nova versão no painel lateral e veja o número de versão e a impressão digital mudarem.

### 3. Acesso efetivo — a razão por trás da permissão (90 s)

O painel **Acesso efetivo** explica a decisão de autorização, regra a regra:

- "É o proprietário do documento" — concede ver, descarregar, editar, eliminar, partilhar, aprovar.
- "Classificação confidencial: leitura para o departamento responsável".
- "O perfil do utilizador não inclui estas permissões na organização" — remove `eliminar`, porque o
  perfil *Finance Approver* não tem `documents.delete`.

Isto demonstra o princípio: uma partilha de documento nunca concede mais do que o perfil permite.

### 4. Isolamento por utilizador (60 s)

Termine a sessão e entre como `contacto@northstar.example` (parceiro externo, Northstar Supplies).

Abra **Documentos**: vê exatamente **um** documento — a fatura que lhe foi partilhada explicitamente.
Nenhuma classificação abre documentos a um utilizador externo; só uma permissão explícita o faz.

### 5. Trilho de auditoria do documento (60 s)

De volta como Ricardo Lopes, abra um documento e o separador **Auditoria**. A cronologia junta o
histórico do documento (criação, transições de estado, versões) às ações desta sessão (consulta,
descarregamento). Cada entrada tem autor, data e identificador de correlação; as entradas com
alteração mostram o antes e o depois.

---

## Cenário 1 — Fatura de fornecedor (previsto: Fases 5 a 8)

1. O fornecedor **Northstar Supplies** entra em `/portal` e submete uma fatura.
2. A fatura aparece na caixa de entrada documental da Asteria Group.
3. A IA classifica o documento como *Supplier Invoice*.
4. A IA extrai fornecedor, número, datas, IVA, total, moeda e encomenda associada, com indicadores de
   confiança e campos em falta assinalados.
5. Ana Ferreira valida os campos: aceita em bloco ou campo a campo; cada decisão é auditada.
6. O workflow de aprovação de faturas arranca.
7. Ricardo Lopes (aprovação financeira) recebe a tarefa com prazo e indicador de SLA.
8. Ricardo aprova.
9. O workflow avança: notificação, arquivo.
10. A cronologia mostra cada passo com hora e responsável.
11. Os indicadores do painel refletem a aprovação.
12. O documento fica **Aprovado** e **Arquivado**.

## Cenário 2 — Ciclo de vida de contrato (previsto: Fases 5 a 8)

1. Carregar um contrato.
2. Extrair metadados, incluindo a data de expiração.
3. Iniciar a revisão jurídica (Carla Nunes).
4. Aprovação de direção (Helena Vasconcelos).
5. Pedido de assinatura eletrónica — **demonstração, sem validade legal**.
6. Estado assinado, com *hash* do documento e versão registados.
7. Arquivo.
8. Trilho de auditoria completo, do carregamento ao arquivo.

---

## Pontos a destacar numa entrevista técnica

| Momento | O que demonstra |
| --- | --- |
| Cookie `HttpOnly` e ausência em `document.cookie` | Decisão de segurança consciente sobre armazenamento de sessão |
| Mesma rota, organizações diferentes | Isolamento multi-tenant imposto no servidor |
| `403 origin_not_allowed` | Proteção CSRF sem gestão de *tokens* no cliente |
| Permissões visíveis no painel | Autorização por permissão, guardada em base de dados |
| Auditoria imutável com correlação | Conformidade e diagnóstico ligados pelo mesmo identificador |
| Testes de integração com SQL Server real | Os testes verificam comportamento relacional verdadeiro |
| Painel de acesso efetivo | Autorização por recurso explicável, não apenas um sim/não |
| Impressão digital por versão | Integridade verificável e base para a assinatura eletrónica |
| Filtro aplicado dentro da consulta SQL | A contagem total não revela documentos que o utilizador não pode ver |
