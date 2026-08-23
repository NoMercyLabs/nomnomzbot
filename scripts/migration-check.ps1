# Migration gate: prove a migration works on an UPGRADE, not just on an empty database.
#
# Unit and integration tests never run the migration pipeline against a database that already holds
# rows, so a migration that only succeeds on a fresh install passes every test and still bricks every
# existing deployment on upgrade. That is exactly how slice S005's unique index took the default
# self-host profile down (SQLite Error 19 at "Running database migrations") while 4460 tests were green.
#
#   scripts/migration-check.ps1                      # SQLite (the default self-host runtime)
#   scripts/migration-check.ps1 -Provider Postgres   # needs `docker compose up -d postgres`
#   scripts/migration-check.ps1 -SeedSql "INSERT ..." # rows to plant before the upgrade leg
#
# What it does: migrates a throwaway database to the previous migration, optionally seeds rows that the
# newest migration must cope with, then applies the newest migration and reports whether it survived.

param(
    [ValidateSet('Sqlite', 'Postgres')][string]$Provider = 'Sqlite',
    [string]$SeedSql,
    # Postgres only. Defaults to the running container's own credential, which drifts from .env on
    # long-lived dev containers (a container keeps the password it was CREATED with).
    [string]$PostgresContainer = 'nomnomzbot-postgres'
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path
$server = Join-Path $repo 'server'
$infra = 'src/NomNomzBot.Infrastructure'
$api = 'src/NomNomzBot.Api'

function Invoke-Ef([string[]]$EfArgs, [hashtable]$EnvVars) {
    foreach ($k in $EnvVars.Keys) { Set-Item -Path "env:$k" -Value $EnvVars[$k] }
    Push-Location $server
    try {
        dotnet ef @EfArgs --project $infra --startup-project $api
        if ($LASTEXITCODE -ne 0) { throw "dotnet ef $($EfArgs -join ' ') failed" }
    }
    finally { Pop-Location }
}

[hashtable]$envVars = @{}
if ($Provider -eq 'Postgres') {
    [string]$pw = (docker exec $PostgresContainer printenv POSTGRES_PASSWORD).Trim()
    if (-not $pw) { throw "could not read POSTGRES_PASSWORD from container $PostgresContainer - is it running?" }
    $envVars['ConnectionStrings__DefaultConnection'] =
        "Host=127.0.0.1;Port=5432;Database=nomnomzbot;Username=nomnomzbot;Password=$pw"
    $envVars['Deployment__Mode'] = 'self_host_full'
}
else {
    [string]$dataDir = Join-Path ([System.IO.Path]::GetTempPath()) ("nnz-migcheck-" + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $dataDir | Out-Null
    $envVars['NOMNOMZ_DATA_DIR'] = $dataDir
    Write-Host "== throwaway SQLite data dir: $dataDir =="
}

Write-Host '== migrating to the PREVIOUS migration (the state an existing install is in) =='
Push-Location $server
[string[]]$migrations = (dotnet ef migrations list --project $infra --startup-project $api --no-build 2>$null) |
    Where-Object { $_ -match '^\d{14}_' }
Pop-Location
if ($migrations.Count -lt 2) { throw 'need at least two migrations to test an upgrade' }
[string]$newest = $migrations[-1].Trim()
[string]$previous = $migrations[-2].Trim()
Write-Host "   previous = $previous"
Write-Host "   newest   = $newest"

Invoke-Ef @('database', 'update', $previous) $envVars

if ($SeedSql) {
    Write-Host '== seeding rows the newest migration must cope with =='
    if ($Provider -eq 'Postgres') { docker exec $PostgresContainer psql -U nomnomzbot -d nomnomzbot -c $SeedSql }
    else { Write-Warning 'SeedSql on SQLite: run it against the file in the data dir above, then re-run with -SeedSql omitted' }
}

Write-Host '== applying the NEWEST migration over that populated database =='
Invoke-Ef @('database', 'update', $newest) $envVars

Write-Host "MIGRATION CHECK OK ($Provider) - the upgrade path survives a populated database"
