using System.Security.Cryptography;
using System.Text;

namespace ApiEasyStay.Properties.Services;

public sealed class SyncSecurity
{
    private readonly byte[] _codeKey;
    public TimeSpan TokenLifetime { get; }
    public TimeSpan CodeCooldown { get; }

    public SyncSecurity(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Sync:CodeHashKey"];
        if (string.IsNullOrWhiteSpace(configured) && !environment.IsDevelopment())
            throw new InvalidOperationException("Configure Sync:CodeHashKey com 32 bytes aleatorios em Base64.");
        _codeKey = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Convert.FromBase64String(configured);
        if (_codeKey.Length < 32)
            throw new InvalidOperationException("Sync:CodeHashKey deve conter pelo menos 32 bytes.");
        TokenLifetime = TimeSpan.FromDays(Math.Clamp(configuration.GetValue("Sync:TokenLifetimeDays", 90), 1, 365));
        var cooldown = configuration.GetValue("Sync:CodeCooldownSeconds", 60);
        CodeCooldown = TimeSpan.FromSeconds(environment.IsDevelopment() ? Math.Max(0, cooldown) : Math.Max(60, cooldown));
    }

    public string HashCode(string identifier, string documentHash, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_codeKey,
            Encoding.UTF8.GetBytes($"{identifier}:{documentHash}:{code}")));
}
