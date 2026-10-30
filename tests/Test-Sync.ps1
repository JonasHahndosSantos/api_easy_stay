param(
    [string]$ApiBaseUrl = 'http://127.0.0.1:5000',
    [string]$ConnectionString = $env:ConnectionStrings__DefaultConnection
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $settingsFile = Join-Path $root 'appsettings.Development.json'
    if (Test-Path -LiteralPath $settingsFile) {
        $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        $ConnectionString = $settings.ConnectionStrings.DefaultConnection
    }
    $secretFile = Join-Path $env:APPDATA 'Microsoft\UserSecrets\ApiEasyStay-Local-2026\secrets.json'
    if (Test-Path -LiteralPath $secretFile) {
        $secrets = Get-Content -LiteralPath $secretFile -Raw | ConvertFrom-Json
        if (![string]::IsNullOrWhiteSpace($secrets.'ConnectionStrings:DefaultConnection')) {
            $ConnectionString = $secrets.'ConnectionStrings:DefaultConnection'
        }
    }
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Configure ConnectionStrings__DefaultConnection ou os User Secrets da API.'
}

$workspace = [guid]::NewGuid()
$device = [guid]::NewGuid()
$secondDevice = [guid]::NewGuid()
$key = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
$headers = @{ 'X-EasyStay-Key' = $key }
$base = $ApiBaseUrl.TrimEnd('/') + '/api/v1/sync'
$authWorkspace = $null
$authEmail = "sync-test-$([guid]::NewGuid().ToString('N'))@example.test"

function New-TestCpf {
    $digits = @(for ($i = 0; $i -lt 9; $i++) { Get-Random -Minimum 0 -Maximum 10 })
    foreach ($length in @(9, 10)) {
        $sum = 0
        for ($i = 0; $i -lt $length; $i++) {
            $sum += $digits[$i] * ($length + 1 - $i)
        }
        $check = ($sum * 10) % 11
        $digits += $(if ($check -eq 10) { 0 } else { $check })
    }
    return ($digits -join '')
}

function Send-Events($events, [bool]$atomic = $false, [guid]$deviceId = $device) {
    $body = @{
        espacoId = $workspace
        dispositivoId = $deviceId
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
            clienteId = $script:reservationCustomer
            dataEntrada = '2026-12-20'
            dataSaida = '2026-12-23'
            status = $status
            'quantidadeHóspedes' = 1
            valorTotal = 100
        }
    }
}

function Send-ConcurrentRequests($route, $bodies, $requestHeaders = @{}) {
    $client = [System.Net.Http.HttpClient]::new()
    $requests = @()
    try {
        $tasks = @(foreach ($body in $bodies) {
            $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$base/$route")
            $request.Content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, 'application/json')
            foreach ($header in $requestHeaders.GetEnumerator()) {
                [void]$request.Headers.TryAddWithoutValidation($header.Key, $header.Value)
            }
            $requests += $request
            $client.SendAsync($request)
        })
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try {
                [pscustomobject]@{
                    Status = [int]$response.StatusCode
                    Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                }
            } finally { $response.Dispose() }
        }
    } finally {
        foreach ($request in $requests) { $request.Dispose() }
        $client.Dispose()
    }
}

try {
    $cpf = New-TestCpf
    $phoneRequest = @{identificador='51999999999'; documentoEmpresa=$cpf} | ConvertTo-Json
    $phoneResult = Invoke-WebRequest -Method Post -Uri "$base/auth/request-code" -ContentType 'application/json' -Body $phoneRequest -SkipHttpErrorCheck
    if ($phoneResult.StatusCode -ne 400) { throw 'A API aceitou telefone para envio de codigo.' }
    $phoneVerify = @{identificador='51999999999'; codigo='123456'; dispositivoId=$device} | ConvertTo-Json
    $phoneVerifyResult = Invoke-WebRequest -Method Post -Uri "$base/auth/verify-code" -ContentType 'application/json' -Body $phoneVerify -SkipHttpErrorCheck
    if ($phoneVerifyResult.StatusCode -ne 400) { throw 'A API aceitou telefone para validar codigo.' }
    $invalidDocument = Invoke-WebRequest -Method Post `
        -Uri "$base/company-document/check" -ContentType 'application/json' `
        -Body '{"documentoEmpresa":"12345678901"}' -SkipHttpErrorCheck
    if ($invalidDocument.StatusCode -ne 400) {
        throw 'A API aceitou um CPF inválido.'
    }

    $authRequest = @{
        identificador = $authEmail
        documentoEmpresa = $cpf
        nomeDispositivo = 'primeiro-dispositivo'
    } | ConvertTo-Json
    $challenge = Invoke-RestMethod -Method Post -Uri "$base/auth/request-code" `
        -ContentType 'application/json' -Body $authRequest
    if ($challenge.deliveredBy -ne 'development-console' -or
        $challenge.codigoDesenvolvimento -notmatch '^\d{6}$') {
        throw 'A API local não retornou o código de desenvolvimento.'
    }
    $verifyRequest = @{
        identificador = $authEmail
        documentoEmpresa = $cpf
        codigo = $challenge.codigoDesenvolvimento
        dispositivoId = $device
        nomeDispositivo = 'primeiro-dispositivo'
        nomeEmpresa = 'Pousada teste'
    } | ConvertTo-Json
    $verified = Invoke-RestMethod -Method Post -Uri "$base/auth/verify-code" `
        -ContentType 'application/json' -Body $verifyRequest
    $authWorkspace = [guid]$verified.espacoId
    if ($authWorkspace -eq [guid]::Empty -or
        [string]::IsNullOrWhiteSpace($verified.accessKey)) {
        throw 'A API não criou a pousada e a chave de acesso após validar o código.'
    }
    $rejectedContact = @{
        identificador = "intruso-$([guid]::NewGuid().ToString('N'))@example.test"
        documentoEmpresa = $cpf
        nomeDispositivo = 'outro-dispositivo'
    } | ConvertTo-Json
    $rejected = Invoke-WebRequest -Method Post -Uri "$base/auth/request-code" `
        -ContentType 'application/json' -Body $rejectedContact -SkipHttpErrorCheck
    if ($rejected.StatusCode -ne 401) {
        throw 'Outro contato conseguiu pedir um código para a mesma pousada.'
    }
    $secondChallenge = Invoke-RestMethod -Method Post -Uri "$base/auth/request-code" `
        -ContentType 'application/json' -Body $authRequest
    $secondVerify = @{
        identificador = $authEmail
        documentoEmpresa = $cpf
        codigo = $secondChallenge.codigoDesenvolvimento
        dispositivoId = $secondDevice
        nomeDispositivo = 'segundo-dispositivo'
    } | ConvertTo-Json
    $parallelCodes = @(Send-ConcurrentRequests 'auth/verify-code' @($secondVerify, $secondVerify))
    if (@($parallelCodes | Where-Object Status -EQ 200).Count -ne 1 -or
        @($parallelCodes | Where-Object Status -EQ 401).Count -ne 1) {
        throw 'O mesmo código foi consumido duas vezes em requisições simultâneas.'
    }
    $secondVerified = ($parallelCodes | Where-Object Status -EQ 200).Body
    if ([guid]$secondVerified.espacoId -ne $authWorkspace -or
        [string]::IsNullOrWhiteSpace($secondVerified.accessKey)) {
        throw 'O segundo dispositivo não entrou na mesma pousada.'
    }

    $firstHeaders = @{ 'X-EasyStay-Key' = $verified.accessKey }
    $secondHeaders = @{ 'X-EasyStay-Key' = $secondVerified.accessKey }
    $begin = @{ espacoId = $authWorkspace; dispositivoId = $device } | ConvertTo-Json
    $firstBootstrap = Invoke-RestMethod -Method Post -Uri "$base/bootstrap" `
        -Headers $firstHeaders -ContentType 'application/json' -Body $begin
    if ($firstBootstrap.source -ne 'local' -or $firstBootstrap.completed) {
        throw 'O primeiro dispositivo não recebeu a autorização da carga inicial.'
    }
    $secondBegin = @{ espacoId = $authWorkspace; dispositivoId = $secondDevice } | ConvertTo-Json
    $blockedBootstrap = Invoke-WebRequest -Method Post -Uri "$base/bootstrap" `
        -Headers $secondHeaders -ContentType 'application/json' -Body $secondBegin -SkipHttpErrorCheck
    $blockedSnapshot = Invoke-WebRequest -Uri "$base/snapshot?espacoId=$authWorkspace" `
        -Headers $secondHeaders -SkipHttpErrorCheck
    if ($blockedBootstrap.StatusCode -ne 409 -or $blockedSnapshot.StatusCode -ne 409) {
        throw 'O segundo aparelho conseguiu baixar uma primeira carga incompleta.'
    }
    $firstCustomer = [guid]::NewGuid()
    $firstBusiness = [guid]::NewGuid()
    $firstRole = [guid]::NewGuid()
    $firstUser = [guid]::NewGuid()
    $firstLoad = @{
        espacoId = $authWorkspace; dispositivoId = $device
        eventos = @(@{ id = [guid]::NewGuid(); entidade = 'dados_empresa'; entidadeId = $firstBusiness
            operacao = 'criar'; dados = @{ id = $firstBusiness; nome = 'Pousada teste'; documento = $cpf; email = $authEmail } },
            @{ id = [guid]::NewGuid(); entidade = 'clientes'; entidadeId = $firstCustomer
            operacao = 'criar'; dados = @{ id = $firstCustomer; nome = 'Cliente CPF'; Cpf = '52998224725' } },
            @{ id = [guid]::NewGuid(); entidade = 'perfis'; entidadeId = $firstRole
               operacao = 'criar'; dados = @{ nome = 'Admin'; actions = @('Reservation.View') } },
            @{ id = [guid]::NewGuid(); entidade = 'usuarios'; entidadeId = $firstUser
               operacao = 'criar'; dados = @{ nome = 'Admin'; email = $authEmail; perfilId = $firstRole
               senha = 'pbkdf2_sha256$600000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' } })
    } | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Method Post -Uri "$base/push" -Headers $firstHeaders `
        -ContentType 'application/json' -Body $firstLoad | Out-Null
    $complete = @{ espacoId = $authWorkspace; dispositivoId = $device; concluir = $true } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$base/bootstrap" -Headers $firstHeaders `
        -ContentType 'application/json' -Body $complete | Out-Null
    $secondBootstrap = Invoke-RestMethod -Method Post -Uri "$base/bootstrap" `
        -Headers $secondHeaders -ContentType 'application/json' -Body $secondBegin
    $snapshot = Invoke-RestMethod -Uri "$base/snapshot?espacoId=$authWorkspace" -Headers $secondHeaders
    if ($secondBootstrap.source -ne 'cloud' -or !$secondBootstrap.completed -or
        $snapshot.eventos.Count -ne 4 -or $snapshot.eventos[0].dados.documento -ne $cpf -or
        [string]::IsNullOrWhiteSpace($snapshot.cursor)) {
        throw 'O segundo dispositivo não recebeu uma cópia completa com cursor.'
    }
    $duplicateCpf = @{
        espacoId = $authWorkspace; dispositivoId = $secondDevice
        eventos = @(@{ id = [guid]::NewGuid(); entidade = 'clientes'; entidadeId = [guid]::NewGuid()
            operacao = 'criar'; dados = @{ nome = 'Duplicado'; CPF = '529.982.247-25' } })
    } | ConvertTo-Json -Depth 8
    $duplicate = Invoke-RestMethod -Method Post -Uri "$base/push" -Headers $secondHeaders `
        -ContentType 'application/json' -Body $duplicateCpf
    if ($duplicate.conflitos.Count -ne 1 -or $duplicate.conflitos[0].tipo -ne 'cliente_cpf_duplicado') {
        throw 'A API não identificou o CPF duplicado recebido com caixa/formato diferentes.'
    }
    $duplicateUser = @{espacoId=$authWorkspace; dispositivoId=$secondDevice; eventos=@(@{
        id=[guid]::NewGuid(); entidade='usuarios'; entidadeId=[guid]::NewGuid(); operacao='criar'
        dados=@{nome='Duplicado'; email=$authEmail.ToUpperInvariant(); perfilId=$firstRole
            senha='pbkdf2_sha256$600000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'}
    })} | ConvertTo-Json -Depth 8
    $duplicateUserResult = Invoke-RestMethod -Method Post "$base/push" -Headers $secondHeaders -ContentType 'application/json' -Body $duplicateUser
    if ($duplicateUserResult.conflitos[0].tipo -ne 'usuario_email_duplicado') { throw 'A API aceitou usuarios com e-mail repetido.' }

    $otherCompany = @{ identificador = $authEmail; documentoEmpresa = (New-TestCpf) } | ConvertTo-Json
    $sameContact = Invoke-WebRequest -Method Post -Uri "$base/auth/request-code" `
        -ContentType 'application/json' -Body $otherCompany -SkipHttpErrorCheck
    if ($sameContact.StatusCode -ne 409) { throw 'O mesmo contato foi aceito em duas pousadas.' }
    $wrongDocument = @{
        espacoId = $authWorkspace; dispositivoId = $secondDevice
        eventos = @(@{ id = [guid]::NewGuid(); entidade = 'dados_empresa'; entidadeId = $firstBusiness
            operacao = 'atualizar'; dados = @{ nome = 'Pousada inválida'; documento = (New-TestCpf) } })
    } | ConvertTo-Json -Depth 8
    $wrongCompany = Invoke-WebRequest -Method Post -Uri "$base/push" -Headers $secondHeaders `
        -ContentType 'application/json' -Body $wrongDocument -SkipHttpErrorCheck
    if ($wrongCompany.StatusCode -ne 400) { throw 'A API aceitou trocar o CPF/CNPJ da pousada.' }

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

    $stateUrl = "$base/state?espacoId=$workspace"
    $state = Invoke-RestMethod -Uri $stateUrl -Headers $headers
    if (@($state.entidades | Where-Object entidade -EQ 'clientes').Count -ne 501) {
        throw 'A nuvem não guardou todos os clientes da primeira carga.'
    }
    $replay = Send-Events @($events[0])
    $state = Invoke-RestMethod -Uri $stateUrl -Headers $headers
    if ($replay.eventosAceitos.Count -ne 1 -or @($state.entidades | Where-Object entidade -EQ 'clientes').Count -ne 501) {
        throw 'Reenvio do mesmo evento duplicou o registro.'
    }

    $sharedRoom = [guid]::NewGuid()
    $roomBatch = @(
        @{
            id = [guid]::NewGuid(); entidade = 'quartos'; entidadeId = $sharedRoom
            operacao = 'criar'; dados = @{ id = $sharedRoom; nome = 'Quarto A'; capacidade = 2; status = 0 }
        },
        @{
            id = [guid]::NewGuid(); entidade = 'quartos'; entidadeId = $sharedRoom
            operacao = 'atualizar'; dados = @{ id = $sharedRoom; nome = 'Quarto B' }
        }
    )
    $roomResult = Send-Events $roomBatch
    if ($roomResult.eventosAceitos.Count -ne 2) {
        throw 'A API não aceitou duas alterações do mesmo quarto no mesmo lote.'
    }
    $ordered = Invoke-RestMethod -Uri "$base/pull?espacoId=$workspace&cursor=$([uri]::EscapeDataString($second.cursor))" -Headers $headers
    if ($ordered.eventos[0].dados.nome -ne 'Quarto A' -or $ordered.eventos[1].dados.nome -ne 'Quarto B') {
        throw 'O pull inverteu a ordem dos eventos do mesmo lote.'
    }
    $state = Invoke-RestMethod -Uri $stateUrl -Headers $headers
    $currentRoom = @($state.entidades | Where-Object {
        $_.entidade -eq 'quartos' -and $_.entidadeId -eq $sharedRoom.ToString()
    })
    if ($currentRoom.Count -ne 1 -or
        ($currentRoom[0].dados | ConvertFrom-Json).nome -ne 'Quarto B') {
        throw 'O estado atual do quarto não corresponde à última alteração.'
    }

    $secondDevicePull = Invoke-RestMethod -Uri "$base/pull?espacoId=$workspace" -Headers $headers
    if ($secondDevicePull.eventos.Count -ne 500) {
        throw 'O segundo dispositivo não conseguiu ler a primeira página da nuvem.'
    }

    $roomA = [guid]::NewGuid()
    $roomB = [guid]::NewGuid()
    $raceRoom = [guid]::NewGuid()
    $script:reservationCustomer = $events[0].entidadeId
    Send-Events @(@($roomA, $roomB, $raceRoom) | ForEach-Object {
        @{id=[guid]::NewGuid(); entidade='quartos'; entidadeId=$_.ToString(); operacao='criar'; dados=@{nome='Quarto de teste'; capacidade=2; status=0}}
    }) | Out-Null
    $reservationA = [guid]::NewGuid()
    $reservationB = [guid]::NewGuid()
    $initial = Send-Events @(
        (New-ReservationEvent $reservationA $roomA 1),
        (New-ReservationEvent $reservationB $roomB 1)
    )
    if ($initial.eventosAceitos.Count -ne 2) { throw 'Reservas iniciais rejeitadas.' }

    $raceBodies = @(foreach ($raceDevice in @($device, $secondDevice)) {
        @{ espacoId = $workspace; dispositivoId = $raceDevice; atomico = $true
            eventos = @((New-ReservationEvent ([guid]::NewGuid()) $raceRoom 1)) } | ConvertTo-Json -Depth 8
    })
    $race = @(Send-ConcurrentRequests 'push' $raceBodies $headers)
    if (@($race | Where-Object { $_.Body.eventosAceitos.Count -eq 1 }).Count -ne 1 -or
        @($race | Where-Object { $_.Body.conflitos.Count -eq 1 }).Count -ne 1) {
        throw 'Duas reservas simultâneas conseguiram ocupar o mesmo quarto.'
    }

    $overlap = Send-Events @(
        (New-ReservationEvent ([guid]::NewGuid()) $roomA 1)
    ) $true $secondDevice
    if ($overlap.conflitos.Count -ne 1 -or
        $overlap.conflitos[0].tipo -ne 'reserva_sobreposta') {
        throw 'O segundo dispositivo não recebeu o conflito da reserva.'
    }

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

    $profileId = [guid]::NewGuid()
    $profileResult = Send-Events @(
        @{ id = [guid]::NewGuid(); entidade = 'perfis'; entidadeId = $profileId
           operacao = 'criar'; dados = @{ id = $profileId; nome = 'Recepcao'; perfilInterno = 1;
               actions = @('reservas.listar', 'reservas.criar'); DataHoraCriado = '2026-10-12T12:00:00Z' } },
        @{ id = [guid]::NewGuid(); entidade = 'perfis'; entidadeId = $profileId
           operacao = 'atualizar'; dados = @{ nome = 'Recepcao atualizada'; actions = @('reservas.listar') } }
    )
    if ($profileResult.eventosAceitos.Count -ne 2) { throw 'Alteração parcial do perfil rejeitada.' }
    $profileSnapshot = Invoke-RestMethod -Uri "$base/snapshot?espacoId=$workspace" -Headers $headers
    $profile = @($profileSnapshot.eventos | Where-Object entidadeId -EQ $profileId.ToString())
    if ($profile.Count -ne 1 -or $profile[0].dados.perfilInterno -ne 1 -or
        !$profile[0].dados.DataHoraCriado -or $profile[0].dados.actions.Count -ne 1) {
        throw 'O snapshot perdeu campos do perfil após uma alteração parcial.'
    }

    $upperRoom = New-ReservationEvent ([guid]::NewGuid()) $raceRoom.ToString().ToUpperInvariant() 1
    $caseConflict = Send-Events @($upperRoom) $true
    if ($caseConflict.conflitos.Count -ne 1) { throw 'O UUID em maiúsculas permitiu uma reserva sobreposta.' }

    $deletedId = [guid]::NewGuid()
    Send-Events @(@{ id = [guid]::NewGuid(); entidade = 'clientes'; entidadeId = $deletedId
        operacao = 'criar'; dados = @{ id = $deletedId; nome = 'Excluido'; dataHoraDeletado = '2026-10-12T12:00:00Z' } }) | Out-Null
    $state = Invoke-RestMethod -Uri $stateUrl -Headers $headers
    if (@($state.entidades | Where-Object entidadeId -EQ $deletedId.ToString()).Count -ne 0) {
        throw 'O estado ativo inclui um registro com data de exclusão.'
    }

    $versioned = @{ id=[guid]::NewGuid(); entidade='quartos'; entidadeId=$sharedRoom; operacao='atualizar'
        versaoBase=$roomBatch[1].id; dados=@{nome='Edicao recente'} }
    $versionResult = Send-Events @($versioned)
    $staleEvent = @{id=[guid]::NewGuid(); entidade='quartos'; entidadeId=$sharedRoom; operacao='atualizar'
        versaoBase=$roomBatch[1].id; dados=@{nome='Edicao antiga offline'}}
    $staleResult = Send-Events @($staleEvent)
    if ($versionResult.eventosAceitos.Count -ne 1 -or $staleResult.conflitos[0].tipo -ne 'registro_desatualizado') {
        throw 'Uma edicao offline sobrescreveu um registro mais recente.'
    }
    $orphan = New-ReservationEvent ([guid]::NewGuid()) ([guid]::NewGuid()) 1
    $orphanResult = Send-Events @($orphan)
    if ($orphanResult.conflitos[0].tipo -ne 'dados_invalidos') { throw 'A API aceitou uma reserva sem dependencias.' }
    $invalidStatus = New-ReservationEvent ([guid]::NewGuid()) $sharedRoom 1
    $invalidStatus.dados.status = 'invalido'
    $invalidStatusResult = Send-Events @($invalidStatus)
    if ($invalidStatusResult.conflitos[0].tipo -ne 'dados_invalidos') { throw 'A API nao tratou status malformado.' }
    $otherTenant = Invoke-WebRequest "$base/pull?espacoId=$authWorkspace" -Headers $headers -SkipHttpErrorCheck
    if ($otherTenant.StatusCode -ne 401) { throw 'Uma chave acessou outra pousada.' }
    Invoke-RestMethod -Method Post "$base/auth/revoke" -Headers $secondHeaders -ContentType 'application/json' -Body $secondBegin | Out-Null
    $revoked = Invoke-WebRequest "$base/health?espacoId=$authWorkspace" -Headers $secondHeaders -SkipHttpErrorCheck
    if ($revoked.StatusCode -ne 401) { throw 'A chave revogada continuou funcionando.' }

    $runtime = Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.AspNetCore.App" -Directory | Where-Object Name -Like '10.*' | Sort-Object Name -Descending | Select-Object -First 1
    Add-Type -Path (Join-Path $runtime.FullName 'Microsoft.Extensions.Logging.Abstractions.dll')
    Add-Type -Path (Join-Path $root 'bin\Debug\net10.0\Npgsql.dll')
    $expiryConnection = [Npgsql.NpgsqlConnection]::new($ConnectionString)
    try {
        $expiryConnection.Open()
        $expiry = $expiryConnection.CreateCommand()
        $expiry.CommandText = "UPDATE sync_access_tokens SET data_hora_criado=NOW()-INTERVAL '366 days' WHERE espaco_id=@id;"
        [void]$expiry.Parameters.AddWithValue('id', $authWorkspace)
        [void]$expiry.ExecuteNonQuery()
    } finally { $expiryConnection.Dispose() }
    $expired = Invoke-WebRequest "$base/health?espacoId=$authWorkspace" -Headers $firstHeaders -SkipHttpErrorCheck
    if ($expired.StatusCode -ne 401) { throw 'A chave expirada continuou funcionando.' }
    $bruteChallenge = Invoke-RestMethod -Method Post "$base/auth/request-code" -ContentType 'application/json' -Body $authRequest
    $wrongCode = if ($bruteChallenge.codigoDesenvolvimento -eq '000000') { '111111' } else { '000000' }
    foreach ($attempt in 1..5) {
        $badCode = @{identificador=$authEmail; documentoEmpresa=$cpf; codigo=$wrongCode; dispositivoId=$device} | ConvertTo-Json
        $denied = Invoke-WebRequest -Method Post "$base/auth/verify-code" -ContentType 'application/json' -Body $badCode -SkipHttpErrorCheck
        if ($denied.StatusCode -ne 401) { throw 'Tentativa incorreta foi aceita.' }
    }
    $blockedCode = @{identificador=$authEmail; documentoEmpresa=$cpf; codigo=$bruteChallenge.codigoDesenvolvimento; dispositivoId=$device} | ConvertTo-Json
    $locked = Invoke-WebRequest -Method Post "$base/auth/verify-code" -ContentType 'application/json' -Body $blockedCode -SkipHttpErrorCheck
    if ($locked.StatusCode -ne 401) { throw 'O limite de tentativas foi ignorado.' }
    foreach ($attempt in 1..7) {
        Invoke-RestMethod -Method Post "$base/auth/request-code" -ContentType 'application/json' -Body $authRequest | Out-Null
    }
    $quota = Invoke-WebRequest -Method Post "$base/auth/request-code" -ContentType 'application/json' -Body $authRequest -SkipHttpErrorCheck
    if ($quota.StatusCode -ne 429) { throw 'O limite persistente por contato foi ignorado.' }
    $deletedRoom = [guid]::NewGuid()
    $createdDeleted = Send-Events @(@{id=[guid]::NewGuid(); entidade='quartos'; entidadeId=$deletedRoom; operacao='criar'; dados=@{nome='Quarto excluido'; capacidade=2; status=0}})
    if ($createdDeleted.eventosAceitos.Count -ne 1) { throw 'Quarto de teste nao criado.' }
    $deletedResult = Send-Events @(@{id=[guid]::NewGuid(); entidade='quartos'; entidadeId=$deletedRoom; operacao='excluir'; dados=@{id=$deletedRoom; DataHoraDeletado='2026-10-29T12:00:00Z'}})
    if ($deletedResult.eventosAceitos.Count -ne 1) { throw 'Quarto sem vinculos nao foi excluido.' }
    $deletedReference = Send-Events @((New-ReservationEvent ([guid]::NewGuid()) $deletedRoom 1))
    if ($deletedReference.eventosAceitos.Count -ne 0 -or $deletedReference.conflitos[0].tipo -ne 'dados_invalidos') {
        throw 'A API aceitou uma reserva para quarto excluido.'
    }
    Write-Output 'OK: autenticacao, carga inicial, snapshot, paginacao, idempotencia, concorrencia, rollback, versoes offline, dependencias, isolamento, revogacao e expiracao.'
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
        if ($authWorkspace -ne $null) {
            $command.Parameters.Clear()
            $command.CommandText = @'
DELETE FROM sincronizacoes WHERE espaco_id = @id;
DELETE FROM espacos_sincronizacao WHERE id = @id;
'@
            [void]$command.Parameters.AddWithValue('id', $authWorkspace)
            [void]$command.ExecuteNonQuery()
        }
        $identifierHash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.Encoding]::UTF8.GetBytes($authEmail)))
        $command.Parameters.Clear()
        $command.CommandText = 'DELETE FROM sync_auth_codes WHERE identificador_hash = @hash;'
        [void]$command.Parameters.AddWithValue('hash', $identifierHash)
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}
