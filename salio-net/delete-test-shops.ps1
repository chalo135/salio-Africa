<#
  delete-test-shops.ps1

  Runs delete-test-shops.sql against the DEVELOPMENT database in the Docker
  container salio-db. The shops it deletes are listed in that .sql file
  (step 1): open it to see or change them.

    .\delete-test-shops.ps1            DRY RUN. Prints how many rows would go
                                       from each table, then puts everything back.
    .\delete-test-shops.ps1 -Commit    Really deletes. Asks you to type DELETE first.

  Stop the API before a real delete, so nothing is writing while it runs.
  It never touches a server: it only talks to the local container.
#>
param(
    [switch]$Commit,
    [string]$Container = 'salio-db',
    [string]$Database = 'salio',
    [string]$User = 'salio'
)

$ErrorActionPreference = 'Stop'

$sqlFile = Join-Path $PSScriptRoot 'delete-test-shops.sql'

if (-not (Test-Path $sqlFile)) {
    Write-Host "Cannot find $sqlFile" -ForegroundColor Red
    exit 1
}

$commitValue = 'false'

if ($Commit) {
    Write-Host 'This will REALLY DELETE the shops listed in delete-test-shops.sql from the dev database.' -ForegroundColor Yellow
    $answer = Read-Host 'Type DELETE to go on (anything else stops)'

    if ($answer -cne 'DELETE') {
        Write-Host 'Stopped. Nothing was deleted.'
        exit 1
    }

    $commitValue = 'true'
}
else {
    Write-Host 'DRY RUN: nothing will be deleted. Add -Commit to really delete.' -ForegroundColor Cyan
}

# The file is piped in, so psql inside the container reads it from standard input.
Get-Content -Raw -Encoding UTF8 $sqlFile |
    docker exec -i $Container psql -U $User -d $Database -v ON_ERROR_STOP=1 -v "commit=$commitValue"

if ($LASTEXITCODE -ne 0) {
    Write-Host 'psql reported an error. Nothing was deleted (the script runs in one transaction).' -ForegroundColor Red
    exit $LASTEXITCODE
}
