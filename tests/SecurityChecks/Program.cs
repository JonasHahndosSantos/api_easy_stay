using System.Security.Cryptography;
using System.Text;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

LocalConfigurationChecks.Run();
var production = new TestEnvironment { EnvironmentName = "Production" };
var development = new TestEnvironment { EnvironmentName = "Development" };
IConfiguration Config(params (string Key, string Value)[] entries) => new ConfigurationBuilder()
    .AddInMemoryCollection(entries.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Configuracao insegura aceita."); }
Reject(() => new SyncSecurity(Config(), production));
Reject(() => new SyncSecurity(Config(("Sync:CodeHashKey", Convert.ToBase64String(new byte[8]))), production));
var security = new SyncSecurity(Config(("Sync:CodeHashKey", Convert.ToBase64String(new byte[32])), ("Sync:CodeCooldownSeconds", "0")), production);
Check(security.CodeCooldown.TotalSeconds >= 60, "Cooldown de producao pode ser desativado.");
var hash = security.HashCode("teste@example.test", "document", "123456");
Check(hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("teste@example.test:123456"))), "OTP usa hash sem segredo.");
Check(hash == security.HashCode("teste@example.test", "document", "123456"), "Hash instavel.");
Check(hash != security.HashCode("teste@example.test", "outro", "123456"), "OTP nao vincula documento.");
Check(security.TokenLifetime.TotalDays == 90, "Validade do token inesperada.");
var unavailable = new AuthCodeSender(Config(), production, NullLogger<AuthCodeSender>.Instance);
try { await unavailable.SendAsync("teste@example.test", "123456", default); throw new Exception("Producao simulou envio."); }
catch (InvalidOperationException) { }
var missingLocal = new AuthCodeSender(Config(), development, NullLogger<AuthCodeSender>.Instance);
try { await missingLocal.SendAsync("teste@example.test", "123456", default); throw new Exception("Ausencia de SMTP foi ocultada."); }
catch (InvalidOperationException) { }
var local = new AuthCodeSender(Config(("Sync:UseDevelopmentConsole", "true")), development, NullLogger<AuthCodeSender>.Instance);
Check(await local.SendAsync("teste@example.test", "123456", default) == "development-console", "Canal de teste incorreto.");
try { await local.SendAsync("51999999999", "123456", default); throw new Exception("Telefone foi aceito como e-mail."); }
catch (ArgumentException) { }
var insecure = new AuthCodeSender(Config(("Smtp:Host", "smtp.gmail.com"), ("Smtp:Username", "sender@gmail.com"),
    ("Smtp:Password", "test-app-password"), ("Smtp:EnableSsl", "false")), development, NullLogger<AuthCodeSender>.Instance);
try { await insecure.SendAsync("teste@example.test", "123456", default); throw new Exception("Gmail sem TLS foi aceito."); }
catch (InvalidOperationException) { }
await using (var smtp = new TestSmtpServer())
{
    var sender = new AuthCodeSender(Config(("Smtp:Host", "127.0.0.1"), ("Smtp:Port", smtp.Port.ToString()),
        ("Smtp:EnableSsl", "false"), ("Smtp:Username", "sender@gmail.com"), ("Smtp:Password", "test-app-password")),
        development, NullLogger<AuthCodeSender>.Instance);
    Check(await sender.SendAsync("destino@example.test", "123456", default) == "email", "Envio SMTP nao confirmado.");
    var message = await smtp.Message.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(message.Contains("sender@gmail.com") && message.Contains("destino@example.test") && message.Contains("123456"),
        "Remetente, destinatario ou codigo ausente.");
    Check(smtp.Username == "sender@gmail.com" && smtp.Password == "test-app-password", "Credenciais SMTP nao utilizadas.");
}
Console.WriteLine("OK: configuracao local e precedencia, isolamento de producao, HMAC, cooldown, validade, e-mail exclusivo, Gmail com TLS e entrega SMTP local.");

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "SecurityChecks";
    public string ContentRootPath { get; set; } = ".";
    public string WebRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
