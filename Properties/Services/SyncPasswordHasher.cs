using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ApiEasyStay.Properties.Services;

public static class SyncPasswordHasher
{
    private const int Iterations = 600_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2_sha256${Iterations}${WebEncoders.Base64UrlEncode(salt)}${WebEncoders.Base64UrlEncode(derived)}";
    }

    public static bool Verify(string password, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue)) return false;
        var parts = storedValue.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2_sha256" ||
            !int.TryParse(parts[1], out var iterations) || iterations is < 1 or > 1_000_000)
            return false;

        try
        {
            var salt = WebEncoders.Base64UrlDecode(parts[2]);
            var expected = WebEncoders.Base64UrlDecode(parts[3]);
            if (salt.Length is < 8 or > 64 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
