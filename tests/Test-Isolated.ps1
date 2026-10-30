param([string]$ConnectionString = $env:ConnectionStrings__DefaultConnection,
      [string]$AppProject, [int]$Port = 5187,
      [string]$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$ConnectionString) {
    $settings = Get-Content (Join-Path $root 'appsettings.Development.json') -Raw | ConvertFrom-Json
    $ConnectionString = $settings.ConnectionStrings.DefaultConnection
    $secretPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\ApiEasyStay-Local-2026\secrets.json'
    if (Test-Path $secretPath) {
        $secrets = Get-Content $secretPath -Raw | ConvertFrom-Json
        if ($secrets.'ConnectionStrings:DefaultConnection') { $ConnectionString = $secrets.'ConnectionStrings:DefaultConnection' }
    }
}
if (!$ConnectionString) { throw 'Informe a conexao de um PostgreSQL local de testes.' }
$parts = [System.Data.Common.DbConnectionStringBuilder]::new()
$parts.set_ConnectionString($ConnectionString)
$dbHost = [string]$parts.get_Item('Host')
$dbPort = if ($parts.ContainsKey('Port')) { [string]$parts.get_Item('Port') } else { '5432' }
if ($dbHost -notin @('localhost', '127.0.0.1', '::1')) { throw 'Este teste descartavel aceita apenas PostgreSQL local.' }
$dbUser = [string]$parts.get_Item('Username')
$database = 'easystay_review_' + [guid]::NewGuid().ToString('N')
$saved = @{}
foreach ($key in @('PGPASSWORD','ConnectionStrings__DefaultConnection','ASPNETCORE_ENVIRONMENT','Sync__CodeCooldownSeconds','Sync__UseDevelopmentConsole','EASY_STAY_TEST_API_URL')) {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
$server = $null
$created = $false
try {
    $env:PGPASSWORD = [string]$parts.get_Item('Password')
    & $Psql -w -X -h $dbHost -p $dbPort -U $dbUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $database;"
    if ($LASTEXITCODE) { throw 'Falha ao criar banco descartavel.' }
    $created = $true
    $parts.set_Item('Database', $database)
    $testConnection = $parts.get_ConnectionString()
    $env:ConnectionStrings__DefaultConnection = $testConnection
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Sync__CodeCooldownSeconds = '0'
    $env:Sync__UseDevelopmentConsole = 'true'
    $url = "http://127.0.0.1:$Port"
    $logs = Join-Path $root 'test-output'
    New-Item -ItemType Directory -Force $logs | Out-Null
    $server = Start-Process -FilePath (Join-Path $root 'bin\Debug\net10.0\ApiEasyStay.exe') -ArgumentList @('--urls', $url) -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'api.stdout.log') -RedirectStandardError (Join-Path $logs 'api.stderr.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        if ($server.HasExited) { throw 'A API de teste encerrou. Consulte test-output/api.stderr.log.' }
        try { $health = Invoke-WebRequest "$url/health/ready" -TimeoutSec 1; if ($health.StatusCode -eq 200) { $ready=$true; break } } catch {}
        Start-Sleep -Milliseconds 200
    }
    if (!$ready) { throw 'A API de teste nao ficou pronta.' }
    & (Join-Path $PSScriptRoot 'Test-Sync.ps1') -ApiBaseUrl $url -ConnectionString $testConnection
    if ($AppProject) {
        $env:EASY_STAY_TEST_API_URL = $url
        Push-Location $AppProject
        try {
            & flutter test --no-pub test/sync_live_api_test.dart --reporter expanded
            if ($LASTEXITCODE) { throw 'Falha no teste do app contra a API real.' }
        } finally { Pop-Location }
    }
    Write-Output 'OK: API e app validados em banco descartavel.'
} finally {
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
    if ($created -and $database -match '^easystay_review_[a-f0-9]{32}$') {
        & $Psql -w -X -h $dbHost -p $dbPort -U $dbUser -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE $database WITH (FORCE);"
    }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
