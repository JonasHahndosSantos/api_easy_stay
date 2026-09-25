param(
    [string]$ApiBaseUrl = 'http://127.0.0.1:5191',
    [string]$ConnectionString = $env:ConnectionStrings__DefaultConnection
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $secretFile = Join-Path $env:APPDATA 'Microsoft\UserSecrets\ApiEasyStay-Local-2026\secrets.json'
    $secrets = Get-Content $secretFile -Raw | ConvertFrom-Json
    $ConnectionString = $secrets.'ConnectionStrings:DefaultConnection'
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Configure ConnectionStrings__DefaultConnection ou os User Secrets da API.'
}

$workspace = [guid]::NewGuid()
$device = [guid]::NewGuid()
$key = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
$headers = @{ 'X-EasyStay-Key' = $key }
$base = $ApiBaseUrl.TrimEnd('/') + '/api/v1/sync'

function Send-Events($events, [bool]$atomic = $false) {
    $body = @{
        espacoId = $workspace
        dispositivoId = $device
        atomico = $atomic
        eventos = @($events)
    } | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Method Post -Uri "$base/push" -Headers $headers `
        -ContentType 'application/json' -Body $body
}

function New-ReservationEvent($reservationId, $roomId, [int]$status) {
    @{
        id = [guid]::NewGuid()
        entidade = 'reservas'
        entidadeId = $reservationId
        operacao = 'atualizar'
        dados = @{
            id = $reservationId
            quartoId = $roomId
            dataEntrada = '2026-12-20'
            dataSaida = '2026-12-23'
            status = $status
        }
    }
}

try {
    $registration = @{ espacoId = $workspace; nomeDispositivo = 'api-smoke' } |
        ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$base/register" -Headers $headers `
        -ContentType 'application/json' -Body $registration | Out-Null

    $events = @()
    for ($i = 0; $i -lt 501; $i++) {
        $customer = [guid]::NewGuid()
        $events += @{
            id = [guid]::NewGuid()
            entidade = 'clientes'
            entidadeId = $customer
            operacao = 'criar'
            dados = @{ id = $customer; nome = "Teste $i" }
        }
    }
    for ($offset = 0; $offset -lt 501; $offset += 250) {
        $last = [Math]::Min($offset + 249, 500)
        $response = Send-Events @($events[$offset..$last])
        if ($response.eventosAceitos.Count -ne ($last - $offset + 1)) {
            throw 'Push incompleto.'
        }
    }
    $first = Invoke-RestMethod -Uri "$base/pull?espacoId=$workspace" -Headers $headers
    $cursor = [uri]::EscapeDataString($first.cursor)
    $second = Invoke-RestMethod -Uri "$base/pull?espacoId=$workspace&cursor=$cursor" `
        -Headers $headers
    if ($first.eventos.Count -ne 500 -or !$first.hasMore -or
        $second.eventos.Count -ne 1 -or $second.hasMore) {
        throw 'Paginação do pull incorreta.'
    }

    $roomA = [guid]::NewGuid()
    $roomB = [guid]::NewGuid()
    $reservationA = [guid]::NewGuid()
    $reservationB = [guid]::NewGuid()
    $initial = Send-Events @(
        (New-ReservationEvent $reservationA $roomA 1),
        (New-ReservationEvent $reservationB $roomB 1)
    )
    if ($initial.eventosAceitos.Count -ne 2) { throw 'Reservas iniciais rejeitadas.' }

    $cancelA = New-ReservationEvent $reservationA $roomA 4
    $failed = Send-Events @(
        $cancelA,
        (New-ReservationEvent ([guid]::NewGuid()) $roomB 1)
    ) $true
    if ($failed.eventosAceitos.Count -ne 0 -or $failed.conflitos.Count -ne 1) {
        throw 'Lote atômico com conflito não foi revertido.'
    }
    $latestUrl = "$base/latest?espacoId=$workspace&entidade=reservas&entidadeId=$reservationA"
    $before = Invoke-RestMethod -Uri $latestUrl -Headers $headers
    if ($before.evento.dados.status -ne 1) { throw 'Rollback parcial detectado.' }

    $resolved = Send-Events @(
        $cancelA,
        (New-ReservationEvent ([guid]::NewGuid()) $roomA 1)
    ) $true
    if ($resolved.eventosAceitos.Count -ne 2) {
        throw 'Resolução atômica válida foi rejeitada.'
    }
    $latest = Invoke-RestMethod -Uri $latestUrl -Headers $headers
    $unauthorized = Invoke-WebRequest -Uri $latestUrl -SkipHttpErrorCheck
    if ($latest.evento.dados.status -ne 4 -or $unauthorized.StatusCode -ne 401) {
        throw 'A rota latest não retornou o estado protegido esperado.'
    }

    Write-Output 'OK: paginação, conflito, rollback atômico e acesso à versão remota.'
}
finally {
    $runtime = Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.AspNetCore.App" `
        -Directory | Where-Object Name -Like '10.*' |
        Sort-Object Name -Descending | Select-Object -First 1
    Add-Type -Path (Join-Path $runtime.FullName 'Microsoft.Extensions.Logging.Abstractions.dll')
    Add-Type -Path (Join-Path $root 'bin\Debug\net10.0\Npgsql.dll')
    $connection = [Npgsql.NpgsqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @'
DELETE FROM sincronizacoes WHERE espaco_id = @id;
DELETE FROM espacos_sincronizacao WHERE id = @id;
'@
        [void]$command.Parameters.AddWithValue('id', $workspace)
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}
