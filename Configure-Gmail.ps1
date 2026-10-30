$ErrorActionPreference = 'Stop'
$apiProject = Join-Path $PSScriptRoot 'ApiEasyStay.csproj'
$email = (Read-Host 'Gmail que enviara os codigos').Trim()
try {
    $address = [System.Net.Mail.MailAddress]::new($email)
} catch {
    throw 'Informe um e-mail valido.'
}
if ($address.Address -ne $email -or !$email.Contains('@')) {
    throw 'Informe um e-mail valido.'
}
$securePassword = Read-Host 'Senha de app do Google (nao e a senha normal do Gmail)' -AsSecureString
if ($securePassword.Length -eq 0) { throw 'A senha de app nao pode ficar vazia.' }
$credential = [System.Management.Automation.PSCredential]::new($email, $securePassword)
try {
    $settings = @{
        'Smtp:Host' = 'smtp.gmail.com'
        'Smtp:Port' = '587'
        'Smtp:EnableSsl' = 'true'
        'Smtp:Username' = $email
        'Smtp:From' = $email
        'Sync:UseDevelopmentConsole' = 'false'
    }
    foreach ($entry in $settings.GetEnumerator()) {
        & dotnet user-secrets set $entry.Key $entry.Value --project $apiProject
        if ($LASTEXITCODE) { throw 'Falha ao salvar a configuracao do Gmail.' }
    }
    & dotnet user-secrets set 'Smtp:Password' ($credential.GetNetworkCredential().Password) --project $apiProject
    if ($LASTEXITCODE) { throw 'Falha ao salvar a senha de app.' }
    Write-Output 'Gmail configurado no armazenamento local de segredos. Reinicie a API para usar a configuracao.'
} finally {
    $credential = $null
    $securePassword.Dispose()
}
