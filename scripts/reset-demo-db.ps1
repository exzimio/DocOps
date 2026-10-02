<#
.SYNOPSIS
    Repõe a base de dados de demonstração no estado semeado.

.DESCRIPTION
    Os testes end-to-end correm contra a base de dados de desenvolvimento, e alguns criam dados
    verdadeiros — é isso que os torna testes e não encenações. O efeito colateral é que o catálogo
    cresce a cada execução: documentos submetidos pelo portal, processos iniciados, pedidos de
    assinatura.

    Isso partiu testes três vezes neste projeto, sempre da mesma maneira: uma asserção sobre uma
    contagem que era verdadeira quando foi escrita e deixou de ser quando outra suite criou dados
    legítimos. As asserções foram corrigidas para verificarem identidade em vez de totais, mas a
    correção de fundo é esta — começar cada execução de uma base conhecida, em vez de confiar em que
    cada teste limpe o que criou.

    Não arranca a API: o Playwright faz isso por si, e ao arrancar ela remigra e semeia de novo.

    Recria as três identidades depois de largar a base. Os utilizadores de base de dados vivem
    dentro dela, e largá-la leva-os com ela: a suíte deixou de arrancar por isto, com a API a
    procurar `app_system` numa base onde já não havia nenhum. Os logins ficam, porque são do
    servidor; os utilizadores não.

.EXAMPLE
    pwsh scripts/reset-demo-db.ps1
    cd tests/e2e; npm run test:clean
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$container = 'docops-sqlserver'
$database = 'DocOpsControlTower'

Write-Host 'A parar a API, se estiver a correr...'
Get-Process -Name 'DocOps.Api' -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "A largar a base de dados $database..."
$drop = "ALTER DATABASE $database SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $database;"
docker exec $container /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Local_Dev_Passw0rd!' -C -Q $drop 2>&1 |
    Out-String |
    Write-Verbose

# O DROP deixa os ficheiros para trás quando a base não estava acessível no momento em que foi
# largada, e o arranque seguinte falha com o erro 5170. Removê-los aqui evita o diagnóstico.
docker exec -u root $container bash -c `
    "rm -f /var/opt/mssql/data/$database.mdf /var/opt/mssql/data/${database}_log.ldf" 2>&1 |
    Out-String |
    Write-Verbose

# As identidades, outra vez. O guião é repetível e cria a base de dados se ela faltar, que é o
# caso aqui: a API liga-se como `app_tenant` e `app_system`, e nenhum dos dois existe numa base
# recém-criada. As palavras-passe são as mesmas do `appsettings.Development.json`, e valem só nesta
# máquina.
Write-Host 'A recriar as identidades da base de dados...'

$identidades = Join-Path $PSScriptRoot 'create-database-identities.sql'

docker cp $identidades "${container}:/tmp/create-database-identities.sql" | Out-Null

docker exec $container /opt/mssql-tools18/bin/sqlcmd `
    -S localhost -U sa -P 'Local_Dev_Passw0rd!' -C -b `
    -v AppTenantPassword='Qx7_Local_Dev_91!' AppSystemPassword='Vb3_Local_Dev_42!' `
    MigratorPassword='Mg5_Local_Dev_73!' `
    -i /tmp/create-database-identities.sql |
    Out-String |
    Write-Verbose

if ($LASTEXITCODE -ne 0) {
    throw "A criação das identidades falhou (código $LASTEXITCODE). Sem elas a API não arranca."
}

$storage = Join-Path $root 'src/Api/storage'

if (Test-Path $storage) {
    Write-Host 'A limpar os ficheiros carregados...'
    Remove-Item -Recurse -Force $storage
}

Write-Host 'Feito. A API semeia de novo no próximo arranque.' -ForegroundColor Green
