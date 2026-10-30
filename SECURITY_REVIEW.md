# Revisao de seguranca e sincronizacao - 2026-10-26

## Modelo de acesso

Cada dispositivo autorizado por codigo recebe um token aleatorio de 256 bits,
limitado a uma hospedagem. O servidor armazena SHA-256 do token, compara hashes
em tempo constante e exige o token nos endpoints de dados. O codigo OTP foi
alterado de SHA-256 simples para HMAC-SHA-256 com segredo exclusivo do servidor,
incluindo contato e documento. Ha validade de dez minutos, cinco tentativas,
consumo unico transacional, cooldown e limite persistente de dez envios/hora/contato.

Tokens expiram em 90 dias por padrao, sao substituidos ao reautorizar o mesmo
dispositivo e podem ser revogados por `POST /api/v1/sync/auth/revoke`. O app tenta
revogar ao remover a conexao e consegue desconectar localmente mesmo offline;
nesse caso, o token remoto permanece valido ate revogacao no servidor ou expiracao.
A chave compartilhada da rota antiga de registro nao e aceita em Production.

O token e uma credencial de dispositivo confiavel e concede acesso aos dados
da hospedagem inteira. As permissoes dos funcionarios sao verificadas no app.
Nao existe sessao individual de funcionario no protocolo da API: quem controla
um dispositivo autorizado e seu token pode modificar os dados da conta. Para
clientes nao confiaveis, integracoes publicas ou auditoria por funcionario, deve-se
adicionar autenticacao individual e autorizacao por operacao no servidor.

O contato verificado identifica a conta. Editar telefone/e-mail comercial em
`dados_empresa` nao altera automaticamente esse contato de acesso. Uma troca
de contato de recuperacao precisa de fluxo proprio com verificacao do novo
canal; nao foi implementada como alteracao livre de cadastro.

## Correcoes realizadas

- HTTPS reconhecido apenas de proxies confiaveis, hosts permitidos e PostgreSQL
  com validacao completa do certificado em producao.
- Erros sem SQL, stack trace ou dados pessoais na resposta; respostas de dados
  marcadas `no-store`; limite de corpo e 429 com mensagem legivel.
- Consulta publica de documento retorna apenas existencia/dados, sem expor ID
  interno ou nome da empresa antes da verificacao do contato.
- OTP protegido por segredo, limites por contato, token com validade e revogacao;
  Envio exclusivo por e-mail, via SMTP com TLS. Producao nunca usa
  o codigo de console como substituto de entrega real.
- Dependencias de registros verificadas na mesma hospedagem; senhas em texto
  simples rejeitadas. Atualizacoes parciais sao mescladas antes da validacao.
- Serializacao das escritas por hospedagem com lock transacional PostgreSQL;
  conflitos de quarto/data e CPF continuam atomicos entre dispositivos/replicas.
- Versao-base do registro impede sobrescrever edicao concorrente. O snapshot
  usa o ID real do ultimo evento, igual ao pull. Em producao, alterar um registro
  existente exige `versaoBase`; publique o app atualizado junto com a API.
- Primeira carga ordenada por dependencias, materializada em fila duravel e
  retomada sem reenviar o que ja foi confirmado. Exige usuarios/perfis antes
  de concluir; outro dispositivo nao recebe snapshot de carga incompleta.
- Uma base local recriada nao confia apenas no marcador antigo das preferencias:
  uma nuvem concluida e baixada com a confirmacao de substituicao.
- App permite trabalho offline apos a primeira carga, preserva alteracoes locais
  em conflito e permite corrigir/reenviar. Cliente/reserva online sao validados
  e confirmados na nuvem antes da gravacao local.
- Empresa usa SQLite como fonte principal; migracao de preferencias antigas
  ocorre uma vez em bases existentes. Base nova nao ressuscita cadastro antigo.
- Novas senhas usam PBKDF2-HMAC-SHA256 com salt aleatorio e 600.000 iteracoes.
  Hashes antigos continuam verificaveis e sao atualizados no login. Calculo em
  isolate evita travar a interface.

## Protecao dos dados e limites

HTTPS protege transporte. A API precisa ler campos para verificar CPF, reservas e
dependencias, portanto nao e criptografia ponta a ponta. Os payloads JSONB, eventos
e backups contem dados pessoais; habilite criptografia em repouso do PostgreSQL,
disco e backups no provedor. O aplicativo tambem usa SQLite local sem SQLCipher;
proteja o aparelho, o perfil Windows e os backups. Tokens do app ficam no armazenamento
seguro do sistema. Hash de CPF/contato serve para busca, nao substitui criptografia.

Nao ha configuracao de infraestrutura aplicada por esta revisao: dominio, certificado,
proxy, cofre, banco criptografado, conta SMTP, backups e alertas precisam
ser configurados e testados no destino. Docker foi preparado, mas o container e
a entrega real de mensagens nao foram executados aqui.

A primeira carga parcial continua pertencendo ao dispositivo que a iniciou.
Ele pode retomar o envio. Se esse dispositivo for perdido antes da conclusao,
e necessario recuperar sua base/identidade ou realizar recuperacao operacional
controlada; a API nao descarta automaticamente dados parciais para outro aparelho.

O journal e o snapshot completo podem crescer. Antes de bases muito grandes,
avalie snapshot paginado, retencao com cursor recuperavel e indices de consulta
de conflitos. O lock por hospedagem privilegia consistencia e pode limitar a
taxa de escrita de uma unica conta. Limites por IP sao locais a cada replica;
use tambem limite no proxy/WAF.

## Validacoes executadas

Compilacao .NET, verificacao NuGet de vulnerabilidades conhecidas, testes de
HMAC/configuracao/SMTP simulado em loopback, suite HTTP em PostgreSQL descartavel e
teste Flutter contra API real. Os cenarios incluem consumo simultaneo de OTP,
limite de tentativas/envios, token expirado/revogado, isolamento entre hospedagens,
primeira carga, bloqueio de carga parcial, snapshot, paginacao, replay idempotente,
CPF duplicado, duas reservas simultaneas, rollback atomico, dependencias ausentes,
edicao offline concorrente, preservacao local e correcao posterior.

As verificacoes de bibliotecas e testes reduzem riscos; nao constituem certificacao
nem substituem teste de invasao e validacao do ambiente publicado.

## Referencias

- Microsoft: https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- OWASP REST: https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html
- OWASP senhas: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
- Gmail: https://support.google.com/mail/answer/7104828
- Senhas de app: https://support.google.com/mail/answer/185833
