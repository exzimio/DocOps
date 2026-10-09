# Instalação e execução

## 1. Pré-requisitos

| Ferramenta | Versão usada no desenvolvimento | Verificação |
| --- | --- | --- |
| .NET SDK | 10.0.401 | `dotnet --version` |
| Node.js | 24.x | `node --version` |
| Docker | com Docker Compose v2 | `docker compose version` |

Nada mais é necessário: não é preciso instalar SQL Server nem qualquer conta de nuvem.

## 2. Configuração

```bash
cp .env.example .env
```

O ficheiro `.env` só é lido pelo `docker compose`. A API lê a sua configuração de
`src/Api/appsettings.Development.json` (já preenchido para o ambiente local) e de variáveis de
ambiente, que têm sempre precedência.

> **Sobre a palavra-passe local.** `Local_Dev_Passw0rd!` é a credencial do container de
> desenvolvimento, ligado apenas a `127.0.0.1`. É deliberadamente pública e serve só para que um
> clone do repositório arranque sem configuração adicional. Nenhuma credencial de produção existe no
> repositório.

## 3. Base de dados

```bash
docker compose up -d
docker compose ps           # aguarde o estado "healthy"
```

O container publica o porto **14330** (e não o predefinido 1433) para não colidir com uma instância
de SQL Server já existente na máquina. Para usar outro porto, defina `MSSQL_HOST_PORT` no `.env` e
ajuste a cadeia de ligação.

**A imagem é construída, não puxada.** A oficial do SQL Server não traz o componente de pesquisa de
texto integral, de que a pesquisa documental depende (ADR-005); `docker/sql-server/Dockerfile`
acrescenta-o. A primeira construção demora alguns minutos e as seguintes vêm da cache. Para confirmar
que ficou instalado:

```bash
docker exec docops-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa   -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT SERVERPROPERTY('IsFullTextInstalled')"   # 1
```

Um servidor **sem** o componente também serve: a migração que cria os índices está protegida, a
aplicação arranca na mesma, e a pesquisa passa a comparar por subcadeia, e di-lo em cada
resposta e no ecrã. É o que acontece nos testes de integração, que levantam a imagem oficial.

## 3.1 As três identidades da base de dados

**Sem este passo a API não arranca**, e é de propósito. A política de segurança ao nível da linha
nega por omissão: uma ligação que não declara organização não vê linha nenhuma, e a dispensa exige a
identidade `app_system`, não apenas uma chave no contexto da sessão. Ligar-se como `sa` daria uma
aplicação saudável a ver zero linhas, que é a pior falha possível aqui.

```bash
docker cp scripts/create-database-identities.sql docops-sqlserver:/tmp/
docker exec docops-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b \
  -v AppTenantPassword='Qx7_Local_Dev_91!' AppSystemPassword='Vb3_Local_Dev_42!' \
     MigratorPassword='Mg5_Local_Dev_73!' \
  -i /tmp/create-database-identities.sql
```

As palavras-passe acima são as que o `appsettings.Development.json` já traz, e valem só nesta
máquina. O guião cria a base de dados se ela faltar e pode voltar a correr sem efeito.

| Identidade | Quem a usa | O que pode |
| --- | --- | --- |
| `app_tenant` | Todos os pedidos normais | Ler e escrever dados |
| `app_system` | Autenticação, worker, semeadura | O mesmo, e é o nome que a política aceita para dispensa |
| `docops_migrator` | Só o passo de migração | Alterar o esquema, e é o único que pode |

Em [`SECURITY.md`](SECURITY.md), secção 4.1, está o que foi medido e o que fica por fechar.

## 4. Migrações e dados de demonstração

Em ambiente de desenvolvimento a API aplica as migrações e semeia os dados no arranque
(`Seed:ApplyMigrations` e `Seed:Enabled` a `true` em `appsettings.Development.json`). A semeadura é
idempotente: pode arrancar as vezes que quiser sem duplicar dados nem perder alterações feitas na
interface.

Para executar as migrações manualmente. **A cadeia tem de ser indicada**: sem `--connection`, o
`dotnet ef` liga-se pela configuração da API, que é `app_tenant`, e essa identidade não altera o
esquema.

```bash
dotnet tool restore
dotnet ef database update \
  --project src/Infrastructure/DocOps.Infrastructure.csproj \
  --startup-project src/Api/DocOps.Api.csproj \
  --connection "Server=127.0.0.1,14330;Database=DocOpsControlTower;User Id=docops_migrator;Password=Mg5_Local_Dev_73!;TrustServerCertificate=True;Encrypt=True"
```

Criar uma nova migração:

```bash
dotnet ef migrations add NomeDaMigracao \
  --project src/Infrastructure/DocOps.Infrastructure.csproj \
  --startup-project src/Api/DocOps.Api.csproj \
  --output-dir Persistence/Migrations
```

Recomeçar do zero (apaga os dados locais, incluindo os ficheiros carregados):

```bash
docker compose down -v && docker compose up -d
rm -rf src/Api/storage
```

Depois disso, as identidades da secção 3.1 outra vez: os utilizadores de base de dados vivem dentro
dela, e largá-la leva-os com ela. O `scripts/reset-demo-db.ps1`, que a suíte end-to-end corre antes
de cada execução, faz as duas coisas de seguida.

Os ficheiros dos documentos ficam em `src/Api/storage`, organizados por organização e por documento.
A pasta é ignorada pelo Git.

## 5. Executar

Dois processos, em terminais separados:

```bash
# API  → http://localhost:5180   (OpenAPI em http://localhost:5180/scalar/v1)
dotnet run --project src/Api/DocOps.Api.csproj

# Web  → http://localhost:5173
npm --prefix src/Web install     # apenas na primeira vez
npm --prefix src/Web run dev
```

O **worker** de segundo plano é opcional para a demonstração, porque os dados semeados já incluem tarefas
em atraso e escaladas, mas é o que faz o escalonamento e a retoma de temporizadores acontecerem em
tempo real:

```bash
dotnet run --project src/Worker/DocOps.Worker.csproj
```

O servidor de desenvolvimento do Vite encaminha `/api` e `/health` para a API, de modo que o browser
permanece numa única origem e o cookie de sessão funciona sem configuração de CORS no cliente.

## 6. Credenciais de demonstração

Palavra-passe de todas as contas: `Demo!Pass123`

| Conta | Pessoa | Perfil | Organização |
| --- | --- | --- | --- |
| `admin@docops.example` | Miguel Antunes | System Admin (administrador de plataforma) | Asteria Group |
| `admin@asteria.example` | Helena Vasconcelos | Tenant Admin | Asteria Group |
| `sofia.martins@asteria.example` | Sofia Martins | Manager | Asteria Group |
| `ricardo.lopes@asteria.example` | Ricardo Lopes | Finance Approver | Asteria Group |
| `carla.nunes@asteria.example` | Carla Nunes | Manager (Jurídico) | Asteria Group |
| `ana.ferreira@asteria.example` | Ana Ferreira | Employee | Asteria Group |
| `joao.pereira@asteria.example` | João Pereira | Employee | Asteria Group |
| `contacto@northstar.example` | Tiago Ribeiro | External Partner | Northstar Supplies |
| `admin@vertice.example` | Beatriz Sousa | Tenant Admin | Vértice Industrial |

A segunda organização (**Vértice Industrial**) existe para demonstrar, e testar, o isolamento entre
organizações. Entre com `admin@vertice.example` e confirme que não vê nada da Asteria Group.

## 7. Testes

```bash
# Backend: unitários
dotnet test tests/UnitTests/DocOps.UnitTests.csproj

# Backend: integração (levanta um SQL Server descartável via Testcontainers; requer Docker)
dotnet test tests/IntegrationTests/DocOps.IntegrationTests.csproj

# Frontend: componente e unidades
npm --prefix src/Web test

# End-to-end (requer a base de dados a correr; arranca API e frontend automaticamente)
npm --prefix tests/e2e install   # apenas na primeira vez
npx --prefix tests/e2e playwright install chromium
npm --prefix tests/e2e test
```

## 8. Portões de qualidade

```bash
dotnet build DocOpsControlTower.slnx        # avisos são tratados como erros
dotnet test DocOpsControlTower.slnx
npm --prefix src/Web run typecheck
npm --prefix src/Web run lint
npm --prefix src/Web run build
npm --prefix src/Web run format:check
```

## 9. Variáveis de ambiente

| Variável | Predefinição | Para que serve |
| --- | --- | --- |
| `ConnectionStrings__DocOpsDatabase` | ver `appsettings.Development.json` | Ligação como `app_tenant`, para os pedidos normais |
| `ConnectionStrings__DocOpsSystem` | ver `appsettings.Development.json` | Ligação como `app_system`. **Obrigatória**: sem ela a aplicação recusa arrancar, porque cair para a cadeia normal faria os caminhos de sistema ver zero linhas sem erro |
| `ConnectionStrings__DocOpsMigrator` | ver `appsettings.Development.json` | Ligação como `docops_migrator`. Exigida só por quem migra |
| `Cors__AllowedOrigins__0` | `http://localhost:5173` | Origens autorizadas a chamar a API com credenciais |
| `Seed__ApplyMigrations` | `true` em desenvolvimento | Aplicar migrações no arranque |
| `Seed__Enabled` | `true` em desenvolvimento | Criar os dados de demonstração |
| `Seed__DefaultPassword` | `Demo!Pass123` | Palavra-passe atribuída às contas de demonstração |
| `OpenTelemetry__OtlpEndpoint` | vazio | Exportação OTLP de *traces* e métricas; vazio mantém a telemetria em processo |
| `FileStorage__RootPath` | `storage` | Raiz do armazenamento de ficheiros (relativa ao *content root* da API) |
| `FileStorage__MaxFileSizeBytes` | `26214400` | Dimensão máxima aceite num carregamento (25 MB) |
| `BackgroundJobs__IntervalSeconds` | `60` (30 em desenvolvimento) | Intervalo entre ciclos do worker |
| `BackgroundJobs__EscalateOverdueTasks` | `true` | Escalonamento de tarefas em atraso |
| `BackgroundJobs__ResumeWorkflowTimers` | `true` | Retoma de instâncias paradas num temporizador |
| `MSSQL_SA_PASSWORD` | `Local_Dev_Passw0rd!` | Palavra-passe do container de desenvolvimento |
| `MSSQL_HOST_PORT` | `14330` | Porto publicado pelo container |
| `VITE_API_PROXY_TARGET` | `http://localhost:5180` | Destino do proxy do servidor de desenvolvimento |

## 10. Configuração opcional de IA

Sem configuração, a aplicação usa o fornecedor de IA de demonstração: determinístico, explicável e
sempre identificado como demonstração na interface. Para ligar um fornecedor real, defina as
variáveis de ambiente correspondentes (`Ai__Provider`, `Ai__Endpoint`, `Ai__ApiKey`); nenhuma chave
é guardada no repositório.

## 11. Resolução de problemas

| Sintoma | Causa provável | Resolução |
| --- | --- | --- |
| `A network-related or instance-specific error…` ao ligar à base de dados | `localhost` resolvido para IPv6 enquanto o container publica em IPv4 | Usar `127.0.0.1` na cadeia de ligação (já é o valor predefinido) |
| Porto 1433 ocupado | Outro SQL Server na máquina | O projeto usa 14330; altere `MSSQL_HOST_PORT` se necessário |
| `403` com `code: origin_not_allowed` | Pedido sem cabeçalho `Origin` permitido | Aceder pela aplicação web ou acrescentar a origem a `Cors:AllowedOrigins` |
| Testes de integração falham a arrancar | Docker parado | Iniciar o Docker Desktop e repetir |
| `401` inesperado após alterar permissões | O cookie ainda tem as *claims* antigas | Aguardar até 5 minutos (revalidação do *security stamp*) ou terminar e reiniciar sessão |
| `MSB1009` ao compilar ou testar | A solução está no formato `.slnx`, o do .NET 10 | Indicar `DocOpsControlTower.slnx` nos comandos |
| A compilação falha porque não consegue escrever `DocOps.Api.dll` | A API está a correr e tem a DLL aberta | Parar a API antes de compilar |
| A API recusa arrancar com «Connection string 'DocOpsSystem' is not configured» | Falta a cadeia da identidade `app_system`, que é obrigatória | Configurar as três cadeias (secção 9) e criar as identidades (secção 3.1) |
| A API deixa de arrancar depois de largar a base de dados | Os utilizadores de base de dados vivem dentro dela e foram largados com ela | Correr `scripts/reset-demo-db.ps1`, que recria a base e as três identidades |
| `npm run dev` termina com «Port 5173 is already in use» | Outro servidor de desenvolvimento ocupa o porto, e o Vite está configurado para não mudar de porto sozinho | Libertar o porto, ou correr com `--port 5174` e acrescentar `http://localhost:5174` a `Cors__AllowedOrigins__1` |
| `sqlcmd` dentro do contentor falha no Git Bash com um caminho estranho | O Git Bash traduz `/opt/...` para um caminho do Windows | Correr o comando no PowerShell, ou prefixá-lo com `MSYS_NO_PATHCONV=1` |
