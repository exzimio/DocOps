-- A mesma tabela, lida como administrador. Devolve 0: a segurança ao nível da linha não isenta
-- administradores, e sa não tem dispensa, porque a dispensa exige a identidade app_system.
SET NOCOUNT ON;
SELECT COUNT(*) FROM AuditEvents;
