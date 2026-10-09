# ADR-002: Autenticação por cookie e autorização por permissões

- **Estado:** aceite
- **Data:** 2026-09-18
- **Âmbito:** identidade, sessão, autorização, multi-tenancy

## Contexto

A aplicação é uma SPA servida ao lado de uma API, com utilizadores internos e externos, seis perfis e
requisitos de auditoria. Tem de ser possível acrescentar mais tarde OpenID Connect / Microsoft Entra
ID sem reescrever a autorização.

## Decisões

### 1. Cookie `HttpOnly` em vez de *bearer token* no cliente

O cookie `docops.session` é `HttpOnly`, `SameSite=Lax` e assinado pela proteção de dados do ASP.NET
Core.

*Alternativa rejeitada:* JWT guardado em `localStorage` ou `sessionStorage`. Qualquer XSS passaria a
ser um roubo de sessão completo. Guardar o JWT em memória evita isso mas perde a sessão a cada
recarregamento e complica a renovação. O cookie resolve ambos os problemas e o browser trata da sua
gestão.

*Custo:* exige proteção contra CSRF, resolvida com `SameSite=Lax` e validação de `Origin`
(ver `SECURITY.md`).

### 2. Autorizar por permissão, não por perfil

Os *endpoints* declaram `[HasPermission(Permissions.DocumentsApprove)]`. As permissões são *claims*
de perfil (`AspNetRoleClaims`) e as políticas são geradas a pedido por `PermissionPolicyProvider`.

*Alternativa rejeitada:* `[Authorize(Roles = "Manager,FinanceApprover")]`. Espalha a regra de negócio
por dezenas de atributos e torna qualquer alteração de perfil uma alteração de código.

*Benefício:* um administrador pode alterar o que um perfil faz sem novo *deploy*, e a alteração fica
auditada como qualquer outra.

### 3. Um utilizador pertence a uma organização

`ApplicationUser.TenantId` é obrigatório. Administradores de plataforma
(`IsPlatformAdministrator`) podem operar entre organizações através de serviços dedicados e
auditados.

*Alternativa rejeitada:* adesão N:N (utilizador em várias organizações). Obrigaria a um seletor de
contexto no início de sessão, a resolução de organização em cada pedido e a mais um ponto de falha no
isolamento, para um caso de uso de que a demonstração não precisa.

*Sinal de revisão:* se surgir o requisito de consultores externos com acesso a várias organizações,
introduzir `TenantMembership` e um contexto de organização explícito e auditado.

### 4. `ApplicationUser` deriva de `IdentityUser<Guid>` no projeto de domínio

*Alternativa rejeitada:* entidade de domínio `User` separada do utilizador de autenticação, com
sincronização entre as duas. Duplicaria o mesmo conceito e criaria uma classe permanente de erros
(divergência entre modelos) em troca de pureza arquitetural.

*Custo aceite:* `Domain` referencia `Microsoft.Extensions.Identity.Stores`. É uma dependência de
abstrações de identidade, não de EF Core nem de ASP.NET Core; o domínio continua sem conhecer
persistência ou HTTP.

### 5. Chaves UUIDv7

`Guid.CreateVersion7()` produz identificadores únicos e ordenáveis no tempo, o que evita a
fragmentação do índice clusterizado típica de GUIDs aleatórios em SQL Server, sem abdicar de
identificadores opacos e não adivinháveis (ao contrário de inteiros sequenciais).

## Consequências

- Nenhum *token* de sessão é acessível a JavaScript.
- Autorização e isolamento entre organizações passam a depender exclusivamente de *claims* emitidas
  pelo servidor.
- A migração para OpenID Connect afeta apenas a emissão do *principal*: as *claims* de permissão e de
  organização continuam a ser adicionadas por `DocOpsUserClaimsPrincipalFactory`, e nenhum
  controlador muda.
- Uma permissão revogada deixa de funcionar em até 5 minutos (revalidação do *security stamp*), sem
  necessidade de terminar a sessão.
