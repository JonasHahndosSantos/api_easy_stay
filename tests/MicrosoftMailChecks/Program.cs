using System.Net;
using System.Text;
using System.Text.Json;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var root = Path.Combine(Path.GetTempPath(), "easystay-mail-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var file = Path.Combine(root, "authorization.json");
const string sender = "jonashsantos613@outlook.com";
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
async Task StoreAsync() => await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
{
    clientId = "e04fd6e9-1546-4b69-a67a-c3c6f02a2a2a", sender, refreshToken = "test-refresh-old"
}));
IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Email:Provider"] = "MicrosoftGraph", ["MicrosoftMail:Sender"] = sender, ["MicrosoftMail:TokenFile"] = file
}).Build();
async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Falha do Outlook tratada como envio confirmado."); }
try
{
    await StoreAsync();
    var http = new FakeMicrosoftHandler();
    using (var graph = new MicrosoftGraphMailSender(Config(), new HttpClient(http)))
    {
        var service = new AuthCodeSender(Config(), new TestEnvironment(), NullLogger<AuthCodeSender>.Instance, graph);
        Check(await service.SendAsync("cliente@example.test", "123456", default) == "email", "Envio nao confirmado.");
        Check(await service.SendAsync("outro@example.test", "987654", default) == "email", "Segundo envio falhou.");
        Check(http.Refreshes == 1 && http.Profiles == 1 && http.Messages.Count == 2, "Token nao foi reutilizado.");
        Check(http.Messages[0].Recipient == "cliente@example.test" && http.Messages[0].Body.Contains("123456"), "Destinatario ou codigo incorreto.");
        Check(http.Messages[1].Recipient == "outro@example.test" && http.Messages[1].Body.Contains("987654"), "Destinatario nao acompanha o cadastro do usuario.");
        using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Check(stored.RootElement.GetProperty("refreshToken").GetString() == "test-refresh-renewed", "Renovacao nao persistiu.");
    }
    await StoreAsync();
    var wrong = new FakeMicrosoftHandler { WrongAccount = true };
    using (var graph = new MicrosoftGraphMailSender(Config(), new HttpClient(wrong)))
    {
        await Reject(() => graph.SendAsync("cliente@example.test", "123456", default));
        Check(wrong.Messages.Count == 0, "Conta diferente enviou e-mail.");
    }
    await StoreAsync();
    var denied = new FakeMicrosoftHandler { RejectRefresh = true };
    using (var graph = new MicrosoftGraphMailSender(Config(), new HttpClient(denied)))
    {
        await Reject(() => graph.SendAsync("cliente@example.test", "123456", default));
        Check(denied.Messages.Count == 0, "Autorizacao invalida confirmou envio.");
    }
    await StoreAsync();
    var refused = new FakeMicrosoftHandler { RefuseMail = true };
    using (var graph = new MicrosoftGraphMailSender(Config(), new HttpClient(refused)))
        await Reject(() => graph.SendAsync("cliente@example.test", "123456", default));
    File.Delete(file);
    var missing = new FakeMicrosoftHandler();
    using (var graph = new MicrosoftGraphMailSender(Config(), new HttpClient(missing)))
    {
        await Reject(() => graph.SendAsync("cliente@example.test", "123456", default));
        Check(missing.Refreshes == 0, "Conta nao autorizada tentou acessar a rede.");
    }
    Console.WriteLine("OK: Outlook OAuth, remetente verificado, destinatario por usuario, renovacao persistida, cache e rejeicoes. HTTP simulado; nenhum e-mail real enviado.");
}
finally { Directory.Delete(root, recursive: true); }

sealed class FakeMicrosoftHandler : HttpMessageHandler
{
    public int Refreshes { get; private set; }
    public int Profiles { get; private set; }
    public bool WrongAccount { get; init; }
    public bool RejectRefresh { get; init; }
    public bool RefuseMail { get; init; }
    public List<(string Recipient, string Body)> Messages { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        if (request.RequestUri!.AbsoluteUri == "https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
        {
            Refreshes++;
            if (request.Headers.Authorization is not null) throw new Exception("Bearer vazou para o endpoint de token.");
            var form = await request.Content!.ReadAsStringAsync(cancellation);
            if (!form.Contains("grant_type=refresh_token") || !form.Contains("test-refresh-old")) throw new Exception("Renovacao invalida.");
            return Reply(RejectRefresh ? HttpStatusCode.BadRequest : HttpStatusCode.OK, new
            {
                access_token = "test-access-token", refresh_token = "test-refresh-renewed", scope = "Mail.Send User.Read", expires_in = 3600
            });
        }
        if (request.Headers.Authorization?.Parameter != "test-access-token") throw new Exception("Bearer ausente ou incorreto.");
        if (request.RequestUri.Host == "graph.microsoft.com" && request.RequestUri.AbsolutePath == "/v1.0/me")
        {
            Profiles++;
            return Reply(HttpStatusCode.OK, new { mail = WrongAccount ? "outra@example.test" : "jonashsantos613@outlook.com", userPrincipalName = "test-user" });
        }
        if (request.RequestUri.AbsoluteUri == "https://graph.microsoft.com/v1.0/me/sendMail")
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var message = body.RootElement.GetProperty("message");
            Messages.Add((message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString()!,
                message.GetProperty("body").GetProperty("content").GetString()!));
            return Reply(RefuseMail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted, new { });
        }
        throw new Exception("Chamada inesperada. Este teste nunca acessa a internet.");
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, object body) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "MicrosoftMailChecks";
    public string ContentRootPath { get; set; } = ".";
    public string WebRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
