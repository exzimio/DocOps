-- Os últimos registos de auditoria, lidos como app_system com o modo de sistema declarado.
-- Sem a declaração, ou com outra identidade, a política de segurança ao nível da linha devolve zero
-- linhas: é o que audit-as-administrator.sql mostra.
SET NOCOUNT ON;
EXEC sp_set_session_context @key = N'docops.system', @value = 1;
SELECT TOP 10 CONVERT(varchar(19), OccurredAtUtc, 120) + ' | ' + Action + ' | ' + ActorDisplayName
FROM AuditEvents
ORDER BY OccurredAtUtc DESC;
