/*
    As três identidades com que a aplicação fala com a base de dados.

    Até aqui havia uma só, com privilégios de administrador, e isso tornava a política de segurança
    ao nível da linha uma defesa que dependia de a aplicação se portar bem: quem consegue executar
    SQL arbitrário com privilégios altos desliga a política e passa a ver tudo.

    | Identidade   | Para que serve                        | O que não pode fazer              |
    | ------------ | ------------------------------------- | --------------------------------- |
    | app_tenant   | Pedidos normais                       | Alterar o esquema. Obter dispensa |
    | app_system   | Autenticação, worker, semeadura        | Alterar o esquema                 |
    | docops_migrator | Só o passo de migração             | Nada: tem DDL de propósito        |

    A dispensa da política não é uma chave que se escreva: é uma chave **mais** a identidade certa.
    `USER_NAME() = N'app_system'` é falso para qualquer outra, incluindo `sa`, que mapeia para `dbo`
    em qualquer base de dados. Foi medido, não suposto.

    E `IMPERSONATE` fica por conceder a toda a gente. Quem o tivesse sobre `app_system` ganhava a
    dispensa com um `EXECUTE AS`, e a parede deixava de ser parede.

    Executar ligado como administrador. Cria a base de dados se ela não existir, porque é o
    administrador quem a cria: `docops_migrator` molda o esquema, mas não tem permissão de servidor
    para criar bases. Pode voltar a correr sem efeito, e é isso que o repor da base de
    desenvolvimento faz a cada execução da suíte end-to-end.

        sqlcmd -S 127.0.0.1,14330 -U sa -P <palavra-passe> -C ^
            -v AppTenantPassword=... AppSystemPassword=... MigratorPassword=... ^
            -i scripts/create-database-identities.sql
*/

:setvar BaseDeDados "DocOpsControlTower"

USE [master];
GO

-- A base de dados antes das identidades: os utilizadores vivem dentro dela, e largar a base
-- leva-os com ela. Foi assim que a suíte end-to-end deixou de arrancar: o repor largava a base, o
-- arranque seguinte procurava `app_system`, e já não havia nenhum.
IF DB_ID(N'$(BaseDeDados)') IS NULL
    CREATE DATABASE [$(BaseDeDados)];
GO

IF SUSER_ID(N'app_tenant') IS NULL
    CREATE LOGIN [app_tenant] WITH PASSWORD = N'$(AppTenantPassword)', CHECK_POLICY = ON;
GO

IF SUSER_ID(N'app_system') IS NULL
    CREATE LOGIN [app_system] WITH PASSWORD = N'$(AppSystemPassword)', CHECK_POLICY = ON;
GO

IF SUSER_ID(N'docops_migrator') IS NULL
    CREATE LOGIN [docops_migrator] WITH PASSWORD = N'$(MigratorPassword)', CHECK_POLICY = ON;
GO

USE [$(BaseDeDados)];
GO

-- -------------------------------------------------------------------------------------------
-- app_tenant: lê e escreve dados, e mais nada.
-- -------------------------------------------------------------------------------------------
IF DATABASE_PRINCIPAL_ID(N'app_tenant') IS NULL
    CREATE USER [app_tenant] FOR LOGIN [app_tenant];
GO

IF IS_ROLEMEMBER(N'db_datareader', N'app_tenant') = 0 ALTER ROLE [db_datareader] ADD MEMBER [app_tenant];
IF IS_ROLEMEMBER(N'db_datawriter', N'app_tenant') = 0 ALTER ROLE [db_datawriter] ADD MEMBER [app_tenant];
GO

-- A pesquisa usa CONTAINS e FREETEXT, e o resto da aplicação chama procedimentos do sistema.
GRANT EXECUTE TO [app_tenant];
GO

-- -------------------------------------------------------------------------------------------
-- app_system: o mesmo, e é o nome que o predicado da política reconhece.
--
-- Não ganha privilégios a mais por ser «de sistema»: o que ganha é ser aceite pela dispensa, e só
-- quando a ligação também declara o modo no contexto da sessão.
-- -------------------------------------------------------------------------------------------
IF DATABASE_PRINCIPAL_ID(N'app_system') IS NULL
    CREATE USER [app_system] FOR LOGIN [app_system];
GO

IF IS_ROLEMEMBER(N'db_datareader', N'app_system') = 0 ALTER ROLE [db_datareader] ADD MEMBER [app_system];
IF IS_ROLEMEMBER(N'db_datawriter', N'app_system') = 0 ALTER ROLE [db_datawriter] ADD MEMBER [app_system];
GRANT EXECUTE TO [app_system];
GO

-- -------------------------------------------------------------------------------------------
-- docops_migrator: altera o esquema, e é a única que o faz.
--
-- É o que permite às outras duas não poderem. Enquanto as migrações corriam no arranque da
-- aplicação, a aplicação tinha de poder alterar o esquema, e quem pode alterar o esquema também
-- pode desligar uma política de segurança.
-- -------------------------------------------------------------------------------------------
IF DATABASE_PRINCIPAL_ID(N'docops_migrator') IS NULL
    CREATE USER [docops_migrator] FOR LOGIN [docops_migrator];
GO

IF IS_ROLEMEMBER(N'db_owner', N'docops_migrator') = 0 ALTER ROLE [db_owner] ADD MEMBER [docops_migrator];
GO

-- -------------------------------------------------------------------------------------------
-- O que nenhuma delas pode.
--
-- `IMPERSONATE` dava a quem o tivesse a identidade de outra, e com ela a dispensa. A recusa fica
-- explícita em vez de implícita, para quem ler isto não ter de deduzir a ausência.
-- -------------------------------------------------------------------------------------------
DENY IMPERSONATE ON USER::[app_system] TO [app_tenant];
DENY IMPERSONATE ON USER::[app_system] TO [docops_migrator];
GO

-- E `app_tenant` não altera a política nem a função do predicado, mesmo que alguém lhe conceda mais
-- do que o previsto por engano.
DENY ALTER ANY SECURITY POLICY TO [app_tenant];
DENY ALTER ANY SECURITY POLICY TO [app_system];
GO

-- -------------------------------------------------------------------------------------------
-- A auditoria é imutável, e não só por convenção da aplicação.
--
-- A recusa de `UPDATE` e `DELETE` sobre `AuditEvents` **não está aqui**, e isso é deliberado: este
-- guião corre antes de a base ter esquema, e uma permissão sobre uma tabela precisa da tabela.
-- Está na migração `DenyAuditMutationToApplicationIdentities`, que é quem cria o esquema e por isso
-- é quem responde pelas permissões sobre ele.
-- -------------------------------------------------------------------------------------------

PRINT 'Identidades criadas: app_tenant, app_system, docops_migrator.';
GO
