using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Services;

public sealed class MicrosoftGraphMailSender(IConfiguration configuration, HttpClient client) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;
    private const string Scopes = "https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/User.Read offline_access";

    public async Task SendAsync(string recipient, string code, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var token = await GetAccessTokenAsync(timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = "Codigo de acesso Easy Stay",
                    body = new { contentType = "Text", content = $"Seu codigo Easy Stay e {code}. Ele expira em 10 minutos." },
                    toRecipients = new[] { new { emailAddress = new { address = recipient } } }
                },
                saveToSentItems = false
            });
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _accessToken = null;
                throw new InvalidOperationException("A autorizacao do Outlook expirou. Tente novamente ou autorize a conta Microsoft outra vez.");
            }
            if (response.StatusCode != HttpStatusCode.Accepted)
                throw new InvalidOperationException($"O Outlook nao confirmou o envio do codigo (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Nao foi possivel conectar ao Outlook para enviar o codigo.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("O Outlook demorou para confirmar o envio do codigo.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            if (_accessToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            var path = configuration["MicrosoftMail:TokenFile"];
            var sender = configuration["MicrosoftMail:Sender"]?.Trim();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sender) || !File.Exists(path))
                throw new InvalidOperationException("Autorize o remetente Outlook com docker/api/Connect-Outlook.ps1 antes de enviar codigos.");
            MailAuthorization authorization;
            try
            {
                authorization = JsonSerializer.Deserialize<MailAuthorization>(await File.ReadAllTextAsync(path, cancellation))
                    ?? throw new JsonException();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new InvalidOperationException("Nao foi possivel ler a autorizacao do Outlook. Autorize a conta novamente.");
            }
            if (!Guid.TryParse(authorization.ClientId, out _) || string.IsNullOrWhiteSpace(authorization.RefreshToken) ||
                !string.Equals(sender, authorization.Sender, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A autorizacao Microsoft nao corresponde ao remetente configurado.");

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post,
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = authorization.ClientId,
                    ["refresh_token"] = authorization.RefreshToken,
                    ["grant_type"] = "refresh_token",
                    ["scope"] = Scopes
                })
            };
            using var tokenResponse = await client.SendAsync(tokenRequest, cancellation);
            if (!tokenResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("A Microsoft recusou a autorizacao. Execute Connect-Outlook.ps1 para autorizar a conta novamente.");
            using var tokenJson = await ReadJsonAsync(tokenResponse, cancellation);
            if (!tokenJson.RootElement.TryGetProperty("access_token", out var accessElement) ||
                string.IsNullOrWhiteSpace(accessElement.GetString()))
                throw new InvalidOperationException("A Microsoft nao retornou uma autorizacao valida de envio.");
            var accessToken = accessElement.GetString()!;
            var grantedScopes = tokenJson.RootElement.TryGetProperty("scope", out var scopes) ? scopes.GetString() ?? "" : "";
            if (!grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(scope =>
                scope.Equals("Mail.Send", StringComparison.OrdinalIgnoreCase) || scope.EndsWith("/Mail.Send", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Autorize a permissao Mail.Send para a conta remetente.");

            using var profileRequest = new HttpRequestMessage(HttpMethod.Get,
                "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var profileResponse = await client.SendAsync(profileRequest, cancellation);
            if (!profileResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("Nao foi possivel verificar a conta Microsoft. Autorize tambem a permissao User.Read.");
            using var profile = await ReadJsonAsync(profileResponse, cancellation);
            var email = profile.RootElement.TryGetProperty("mail", out var mail) ? mail.GetString() : null;
            var principal = profile.RootElement.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null;
            if (!string.Equals(sender, email, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sender, principal, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A conta Microsoft autorizada e diferente do remetente configurado.");

            if (tokenJson.RootElement.TryGetProperty("refresh_token", out var refreshed) &&
                !string.IsNullOrWhiteSpace(refreshed.GetString()))
            {
                authorization.RefreshToken = refreshed.GetString()!;
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(authorization), cancellation);
                    File.Move(temporary, path, overwrite: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Nao foi possivel guardar a autorizacao renovada do Outlook. Confira a pasta privada montada no container.");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            var expiresIn = tokenJson.RootElement.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds) ? seconds : 3600;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(expiresIn, 0, 86400));
            _accessToken = accessToken;
            return accessToken;
        }
        finally { _gate.Release(); }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation); }
        catch (JsonException) { throw new InvalidOperationException("A Microsoft retornou uma resposta de autorizacao invalida."); }
    }

    public void Dispose() { _gate.Dispose(); client.Dispose(); }

    private sealed class MailAuthorization
    {
        [JsonPropertyName("clientId")] public string ClientId { get; set; } = "";
        [JsonPropertyName("sender")] public string Sender { get; set; } = "";
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
    }
}
