# Publicacao da API

Use .NET 10 e PostgreSQL com TLS, backup e disco criptografados. A URL do app
deve conter apenas a origem HTTPS, por exemplo `https://api.example.com`.
O `.env` do Flutter e publico dentro do APK: nunca coloque chaves, senhas ou
credenciais de PostgreSQL nele. Depois de trocar a URL, gere e instale outro build.

## Configuracao de producao

Defina no gerenciador de segredos do provedor (ou variaveis protegidas do runtime):

| Nome | Valor necessario |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | Host, porta, banco e usuario de producao; `SSL Mode=VerifyFull` e certificado confiavel |
| `AllowedHosts` | Dominio real da API, sem esquema ou `*`; se houver varios, separe com `;` |
| `Sync__CodeHashKey` | Pelo menos 32 bytes aleatorios em Base64, persistentes entre reinicios e iguais em todas as replicas |
| `ReverseProxy__KnownProxies__0` | IP real do proxy que termina o HTTPS, quando aplicavel |
| `ReverseProxy__KnownNetworks__0` | Alternativa: CIDR estrito da rede dos proxies confiaveis |

Nao use `0.0.0.0/0`, nao limpe as listas de proxies para confiar em toda a Internet
e nao deixe a porta interna do container publicamente acessivel. O middleware
processa `X-Forwarded-Proto` e `X-Forwarded-For` somente de proxies confiaveis.
Consulte o provedor escolhido para identificar a rede correta. A API recusa HTTP
em producao; os endpoints de saude podem ser consultados por HTTP interno.

Gere o segredo uma vez no PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Guarde o resultado no cofre do provedor. Trocar este segredo invalida codigos
pendentes; nao rotaciona nem revoga automaticamente as chaves de dispositivos.
Credenciais que ja estiveram em Git devem ser rotacionadas, mesmo apos removidas.

## Entrega do codigo

Os codigos sao enviados somente por e-mail. Para Gmail, configure
`Smtp__Host=smtp.gmail.com`, `Smtp__Port=587`, `Smtp__EnableSsl=true`,
`Smtp__Username` com seu e-mail completo e `Smtp__Password` com a senha de app.
`Smtp__From` e opcional e usa o Username por padrao. Veja [EMAIL_SETUP.md](EMAIL_SETUP.md).
Producao recusa SMTP sem TLS; Gmail tambem exige TLS nos testes locais.
Para dominio proprio, confirme SPF, DKIM e DMARC e teste a caixa de entrada real.

Sem configurar o SMTP, a API retorna 503 e nao afirma ter enviado o codigo.
O modo de console exige Development e `Sync:UseDevelopmentConsole=true`;
esta desativado por padrao e e ignorado em producao. Nunca publique com
`Development` nem exponha console ou logs de desenvolvimento.

## Build, migracoes e saude

```powershell
dotnet publish -c Release -o publish
dotnet publish/ApiEasyStay.dll --migrate
dotnet publish/ApiEasyStay.dll
```

Ou construa o `Dockerfile` e rode a imagem com os segredos definidos no runtime.
Execute o comando `--migrate` como etapa unica de release antes de iniciar replicas.
Use um usuario de banco com DDL para essa etapa e um usuario limitado ao esquema
da aplicacao no runtime. Producao nao altera o esquema automaticamente.

- `GET /health/live`: processo ativo; nao acessa dados de clientes.
- `GET /health/ready`: banco acessivel e todas as migracoes aplicadas; 503 caso contrario.
- OpenAPI e a rota antiga `sync/register` ficam apenas em Development.

Configure backups criptografados e teste restauracao, alertas para 5xx/429,
retencao e acesso aos logs. Nao registre headers de autenticacao, respostas de
verificacao de codigo, corpos com dados pessoais nem senhas. Aplique limites
tambem no proxy/WAF. O limite por IP e por processo; o limite por contato e no
PostgreSQL e funciona entre replicas.

Nao remova eventos antigos de `sincronizacoes` sem uma politica de ressincronizacao:
dispositivos offline ainda podem precisar deles. Codigos expirados ha mais de sete
dias podem ser removidos por uma rotina operacional; preserve o intervalo de uma
hora utilizado no limite de solicitacoes. Defina retencao legal e exclusao por conta.

## Testes antes de publicar

```powershell
dotnet build
dotnet run --project tests/SecurityChecks/SecurityChecks.csproj
./tests/Test-Isolated.ps1 -AppProject C:/Users/Jonas/Documents/GitHub/reserva_quartos
```

O ultimo comando exige PostgreSQL local e `psql`; cria um banco com prefixo
`easystay_review_`, inicia outra API em 127.0.0.1:5187, executa testes e remove
apenas esse banco no final. Ajuste `-Psql`, `-Port` e `-AppProject` em outra maquina.
Nao aponte testes antigos diretamente para uma base de clientes.

Depois da publicacao, repita em homologacao com dois dispositivos reais: primeira
carga no primeiro; login por codigo no segundo; alteracoes online e offline; disputa
pelo mesmo quarto; edicao simultanea; expiracao/revogacao e entrega real dos codigos.
Verifique certificado, proxy, SMTP, backups e isolamento da rede de banco.
