# ApiEasyStay

API de sincronização do Easy Stay. Os endpoints CRUD genéricos estão desativados
até que exista autorização por usuário e isolamento por hospedagem. As rotas
ativas são `api/v1/sync/register`, `health`, `push` e `pull`.

## Desenvolvimento

Configure o PostgreSQL local e a conexão com User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=easy_stay;Username=SEU_USUARIO;Password=SUA_SENHA"
dotnet ef database update
dotnet run --launch-profile https
```

Em produção, forneça `ConnectionStrings__DefaultConnection` pelo ambiente ou
gerenciador de segredos. A API rejeita HTTP fora do ambiente Development;
configure TLS diretamente ou um proxy reverso confiável que informe o esquema
HTTPS ao ASP.NET Core. Não reutilize a
senha de desenvolvimento. A chave compartilhada da hospedagem deve ser
distribuída somente aos dispositivos autorizados. A API guarda apenas o hash
SHA-256 dessa chave.

As migrações de banco estão em `Properties/Data/Migrations`. A configuração de
sincronização deve apontar para a mesma hospedagem em todos os dispositivos.

Com a API de desenvolvimento em execução, rode `tests/Test-Sync.ps1` para
verificar paginação, conflitos, rollback atômico e autenticação. O teste cria
uma hospedagem temporária e remove seus eventos ao terminar.
