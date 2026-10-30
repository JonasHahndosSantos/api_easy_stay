# ApiEasyStay

API de sincronização do Easy Stay. Os endpoints CRUD genéricos estão desativados
até que exista autorização por usuário e isolamento por hospedagem. As rotas
ativas ficam em `api/v1/sync`: validação de documento, autenticação por código,
`health`, `bootstrap`, `snapshot`, `push`, `pull`, `latest`, `state` e `auth/revoke`.

Publicacao: [DEPLOYMENT.md](DEPLOYMENT.md). Seguranca, correcoes e limites:
[SECURITY_REVIEW.md](SECURITY_REVIEW.md). O `Dockerfile` usa .NET 10 sem usuario root.
Envio de codigos: somente e-mail. Configure seu Gmail com `./Configure-Gmail.ps1`;
o campo do remetente e `Smtp.Username`. Veja [EMAIL_SETUP.md](EMAIL_SETUP.md).

## Desenvolvimento

Configure o PostgreSQL local. A conexao pode ficar em `appsettings.Local.json`:

```powershell
Copy-Item appsettings.Local.example.json appsettings.Local.json
```

Preencha `ConnectionStrings:DefaultConnection` nesse arquivo com a porta, banco,
usuario e senha do seu PostgreSQL. Ele e ignorado pelo Git, carregado somente em
Development e excluido da publicacao. Nao copie o exemplo sobre uma configuracao
local que ja esteja preenchida.

Os User Secrets continuam funcionando e tem prioridade sobre o arquivo local.
Variaveis de ambiente e argumentos da linha de comando tem prioridade sobre ambos.
Como alternativa ao arquivo, configure a conexao com:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=easy_stay;Username=SEU_USUARIO;Password=SUA_SENHA"
```

Inicie na pasta da API:

```powershell
dotnet run
```

Os perfis locais definem `DOTNET_ENVIRONMENT` e `ASPNETCORE_ENVIRONMENT` como
`Development`, mesmo que o terminal tenha herdado outro ambiente. O perfil
`local-network` tambem pode ser escolhido explicitamente. Em Development, a API
aplica as migracoes ao iniciar. Para conferir API e banco, abra
`http://localhost:5000/health/ready` e confira a resposta `{"status":"ready"}`.

Em produção, forneça `ConnectionStrings__DefaultConnection` pelo ambiente ou
gerenciador de segredos. A API rejeita HTTP fora do ambiente Development;
configure TLS diretamente ou um proxy reverso confiável que informe o esquema
HTTPS ao ASP.NET Core. Não reutilize a
senha de desenvolvimento. Cada dispositivo autorizado por código recebe um
token próprio; a API guarda apenas seu hash SHA-256. A rota legada `register`
aceita uma chave compartilhada e deve ficar restrita a migração/testes.

As migrações de banco estão em `Properties/Data/Migrations`. A configuração de
sincronização deve apontar para a mesma hospedagem em todos os dispositivos.

## Protocolo

- Após validar o código, o token fica associado ao dispositivo.
- Tokens expiram em 90 dias por padrao. A remocao da conexao tenta revogar o token.
- Atualizacoes de registros existentes em producao exigem `versaoBase`, o UUID
  `idEvento` da ultima versao recebida. Um envio desatualizado vira pendencia.
  Publique a nova versao do app junto com esta API.
- `POST bootstrap` identifica se este aparelho cria a primeira carga ou baixa
  a nuvem existente. A carga incompleta fica exclusiva do aparelho de origem.
  Ele chama `bootstrap` com `concluir=true` depois de enviar os registros.
- `GET snapshot` lê o estado atual em transação Repeatable Read e devolve o
  cursor correspondente. O app importa tudo em uma transação SQLite e depois
  usa `pull` a partir desse cursor para receber alterações posteriores.
- `push` usa uma transação com advisory lock por hospedagem. A verificação de
  CPF duplicado e reserva sobreposta acontece dentro dessa mesma transação,
  inclusive quando dois dispositivos enviam ao mesmo tempo. Lotes atômicos
  com conflito são revertidos integralmente. Eventos repetidos são idempotentes.
- A ordem dos eventos usa timestamps em microssegundos, compatíveis com o
  PostgreSQL, para preservar a sequência criar/atualizar no mesmo lote.
- `dados_empresa` participa da carga e preserva nome, documento e contatos.
  O CPF/CNPJ não pode ser trocado por outro documento depois de vinculado.

O perfil `local-network` permite HTTP na rede local em Development. Reinicie
a API depois de alterar código. Se a compilação disser que o executável está
em uso, encerre a execução anterior pelo terminal/IDE antes de compilar.

O envio real usa SMTP com TLS. Sem remetente e senha de app configurados, a API
informa a falha e nao simula entrega. O codigo de console e exclusivo de testes:
exige Development e `Sync:UseDevelopmentConsole=true`, desativado por padrao.

Depois de compilar, rode `pwsh -File tests/Test-Isolated.ps1` para validar a API
em outro processo e banco descartavel. Use `-AppProject` com a pasta Flutter
para testar tambem o envio e download reais. O script configura o canal de
codigo simulado e o cooldown de desenvolvimento apenas para esse processo.
As credenciais de producao nao devem ficar em arquivos versionados.
