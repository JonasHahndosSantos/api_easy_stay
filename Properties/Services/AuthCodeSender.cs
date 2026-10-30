using System.Net;
using System.Net.Mail;

namespace ApiEasyStay.Properties.Services;

public sealed class AuthCodeSender(IConfiguration configuration, IWebHostEnvironment environment,
    ILogger<AuthCodeSender> logger, MicrosoftGraphMailSender? microsoftSender = null)
{
    public async Task<string> SendAsync(string identifier, string code, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!MailAddress.TryCreate(identifier, out var recipient) || recipient.Address != identifier ||
            !identifier.Contains('@') || identifier.Any(char.IsWhiteSpace))
            throw new ArgumentException("Informe um e-mail valido para receber o codigo.");

        if (environment.IsDevelopment() && configuration.GetValue("Sync:UseDevelopmentConsole", false))
        {
            logger.LogWarning("Codigo de desenvolvimento Easy Stay: {Code}", code);
            return "development-console";
        }

        if (string.Equals(configuration["Email:Provider"], "MicrosoftGraph", StringComparison.OrdinalIgnoreCase))
        {
            if (microsoftSender is null)
                throw new InvalidOperationException("O servico de envio Outlook nao foi configurado.");
            await microsoftSender.SendAsync(identifier, code, cancellation);
            return "email";
        }

        var host = configuration["Smtp:Host"]?.Trim();
        var username = configuration["Smtp:Username"]?.Trim();
        var password = configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Configure o remetente de e-mail: autorize o Outlook com Connect-Outlook.ps1 ou configure as credenciais SMTP.");

        var sender = configuration["Smtp:From"]?.Trim();
        if (string.IsNullOrWhiteSpace(sender)) sender = username;
        if (!MailAddress.TryCreate(sender, out var senderAddress) || senderAddress.Address != sender)
            throw new InvalidOperationException("Configure um e-mail remetente valido em Smtp:Username.");

        var port = configuration.GetValue("Smtp:Port", 587);
        var tls = configuration.GetValue("Smtp:EnableSsl", true);
        if (port is < 1 or > 65535)
            throw new InvalidOperationException("Configure uma porta SMTP valida.");
        if (!tls && (!environment.IsDevelopment() || host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("O envio de e-mail exige TLS.");

        using var message = new MailMessage
        {
            From = new MailAddress(senderAddress.Address, configuration["Smtp:FromName"] ?? "Easy Stay"),
            Subject = "Codigo de acesso Easy Stay",
            Body = $"Seu codigo Easy Stay e {code}. Ele expira em 10 minutos.",
            IsBodyHtml = false
        };
        message.To.Add(recipient);
        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = tls,
            Credentials = new NetworkCredential(username, password)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await smtp.SendMailAsync(message, timeout.Token);
        return "email";
    }
}
