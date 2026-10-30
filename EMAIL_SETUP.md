# Envio de codigos por e-mail

## Outlook com Microsoft Graph

O remetente local desejado e jonashsantos613@outlook.com. Os destinatarios
continuam sendo os e-mails cadastrados pelos usuarios. O Outlook pessoal usa
OAuth: a senha e digitada somente no login oficial da Microsoft.

No projeto reserva_quartos, siga docker/api/OUTLOOK.md e execute
`./docker/api/Connect-Outlook.ps1` depois de registrar um aplicativo Microsoft
com contas pessoais, fluxo de cliente publico e permissoes delegadas Mail.Send
e User.Read. A API usa Email:Provider=MicrosoftGraph, MicrosoftMail:Sender e
MicrosoftMail:TokenFile. O arquivo de autorizacao e privado, persistente e
montado no container; nao pertence a imagem nem ao Git. A renovacao e automatica.

A autorizacao real depende do registro e consentimento na conta Microsoft.
O assistente nao envia mensagens. O envio acontece somente na solicitacao
normal de um codigo pelo usuario.

Os testes `dotnet run --project tests/MicrosoftMailChecks` usam HTTP simulado
e verificam remetente, destinatario, renovacao, cache e falhas sem enviar e-mail.

## SMTP por outro provedor

# Enviar codigos pelo Gmail

O envio de codigos agora e somente por e-mail. Telefone e WhatsApp podem continuar
no cadastro comercial, mas nao sao canais de verificacao.
Contas antigas vinculadas apenas a telefone precisam de uma vinculacao verificada
de e-mail para novos acessos. Esta alteracao nao troca o contato dessas contas
automaticamente; uma conexao ja autorizada continua com seu token existente.

## Gmail que envia os codigos

No arquivo `appsettings.Development.json`, o campo e **`Smtp.Username`**:

```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "EnableSsl": true,
  "Username": "SEU_EMAIL@gmail.com",
  "FromName": "Easy Stay"
}
```

Para configurar sem gravar credenciais no projeto, abra o PowerShell na pasta
`ApiEasyStay` e execute o assistente recomendado:

```powershell
.\Configure-Gmail.ps1
```

Ele pergunta o Gmail remetente e, em seguida, a senha de app com entrada oculta.
Salva `Smtp:Username`, `Smtp:From` e `Smtp:Password` nos User Secrets da API,
fora do repositorio. Esses valores tem prioridade sobre `appsettings.Development.json`.
Execute o assistente novamente para trocar o remetente. User Secrets e armazenamento
local de desenvolvimento, nao e um cofre criptografado de producao.

No Google, ative a verificacao em duas etapas e gere uma **senha de app**, quando
essa opcao estiver disponivel na conta. Nao use a senha normal do Gmail. Se a
conta nao permitir senhas de app, sera necessario configurar outro meio de
autorizacao, como OAuth, ou outro servico de envio.

- Senhas de app: https://support.google.com/mail/answer/185833?hl=pt-BR
- Configuracao SMTP: https://support.google.com/mail/answer/7104828?hl=pt-BR

Depois de preencher as credenciais, reinicie a API:

```powershell
dotnet run --launch-profile local-network
```

## E-mail que recebe o codigo

No app: **Configuracoes > Dados da pousada > E-mail**. Na primeira ativacao,
use o mesmo e-mail do cadastro do usuario. Outro dispositivo deve usar esse
e-mail em **Ja possuo conta** no login ou **Ja tenho nuvem** na sincronizacao.
O destinatario nao precisa ser Gmail e pode ser diferente do Gmail remetente.

## Verificacao

Com `Sync:UseDevelopmentConsole=false` (padrao), a API precisa de SMTP configurado:
nao simula entrega quando falta Gmail/senha de app. So confirma o envio depois
que o SMTP aceitar a mensagem. Sem entrega, verifique as credenciais, a pasta
de spam, os limites da conta Gmail e eventual bloqueio da porta 587 na hospedagem.

O modo de console continua disponivel apenas em Development com
`Sync:UseDevelopmentConsole=true`, usado pelos testes isolados. Nao habilite esse
modo ao testar o recebimento real. Em producao ele e ignorado.

Na nuvem, configure `Smtp__Host=smtp.gmail.com`, `Smtp__Port=587`,
`Smtp__EnableSsl=true`, `Smtp__Username` e `Smtp__Password` pelo gerenciador
de segredos do provedor. `Smtp__From` e opcional e, quando ausente, usa o Username.
Os User Secrets locais nao sao carregados em Production.
