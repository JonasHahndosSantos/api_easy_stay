# ApiEasyStay

API de sincronização do Easy Stay. Os endpoints CRUD genéricos estão desativados
até que exista autorização por usuário e isolamento por hospedagem. As rotas
ativas ficam em `api/v1/sync`: validação de documento, autenticação por código,
`health`, `bootstrap`, `snapshot`, `push`, `pull`, `latest` e `state`.

## Desenvolvimento

Configure o PostgreSQL local e a conexão com User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=easy_stay;Username=SEU_USUARIO;Password=SUA_SENHA"
dotnet ef database update
dotnet run --launch-profile local-network
```

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

Sem SMTP em Development, os códigos ficam no terminal da API. Não existe
envio real por WhatsApp sem configurar um provedor de entrega. Configure o
SMTP para e-mail; a API deve falhar de forma explícita se não houver entrega
configurada fora do ambiente de desenvolvimento.

Com a API de desenvolvimento em execução, rode
`pwsh -File tests/Test-Sync.ps1 -ApiBaseUrl http://127.0.0.1:5000` para
verificar código de acesso, primeira carga, estado atual, paginação, múltiplos
dispositivos, conflitos e rollback atômico. O teste cria hospedagens temporárias
e remove seus dados ao terminar.
