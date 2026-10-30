using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiEasyStay.Properties.Services;

internal static partial class SyncRecordValidator
{
    public static async Task<string?> ValidateAsync(string entity, JsonElement data,
        Func<string, Guid, Task<bool>> exists,
        Func<Guid, Task<JsonElement?>>? loadRoom = null)
    {
        if (data.EnumerateObject().Select(x => x.Name.ToLowerInvariant()).Distinct().Count() != data.EnumerateObject().Count())
            return "O registro possui campos repetidos com letras maiusculas e minusculas.";
        if (entity is "clientes" or "quartos" or "usuarios" or "perfis" && string.IsNullOrWhiteSpace(Text(data, "nome")))
            return "Informe o nome do registro antes de sincronizar.";
        if (entity == "usuarios")
        {
            var email = Text(data, "email");
            if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
                return "O usuario precisa de um e-mail valido.";
            if (!PasswordHash().IsMatch(Text(data, "senha")))
                return "A senha do usuario deve estar protegida antes de ser enviada. Atualize o app e redefina a senha.";
        }
        if (entity == "quartos")
        {
            if (!PositiveInteger(data, "capacidade")) return "A capacidade deve ser um inteiro maior que zero.";
            if (Property(data, "status") is JsonElement roomStatus &&
                (roomStatus.ValueKind != JsonValueKind.Number || !roomStatus.TryGetInt32(out var state) || state is < 0 or > 2))
                return "O status do quarto e invalido.";
        }
        if (entity == "reservas")
        {
            if (!DateTime.TryParse(Text(data, "dataEntrada"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start) ||
                !DateTime.TryParse(Text(data, "dataSaida"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) || start >= end)
                return "A reserva possui datas invalidas.";
            if (Property(data, "status") is JsonElement status && (status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var value) || value is < 0 or > 4))
                return "O status da reserva e invalido.";
            if (!PositiveInteger(data, "quantidadeHóspedes")) return "A quantidade de hospedes deve ser um inteiro maior que zero.";
            var cancelled = Property(data, "status") is JsonElement st && st.TryGetInt32(out var sv) && sv == 4;
            if (!cancelled && loadRoom is not null && Guid.TryParse(Text(data, "quartoId"), out var roomId))
            {
                var room = await loadRoom(roomId);
                if (room is JsonElement details)
                {
                    if (Property(details, "status") is JsonElement rs && rs.TryGetInt32(out var rsv) && rsv == 2)
                        return "O quarto selecionado esta em manutencao.";
                    if (Property(details, "capacidade") is JsonElement capacity && capacity.TryGetInt32(out var max) &&
                        Property(data, "quantidadeHóspedes")!.Value.GetInt32() > max)
                        return "A quantidade de hospedes excede a capacidade do quarto.";
                }
            }
        }
        foreach (var name in new[] { "valor", "valorTotal", "precoDiaria", "valorPagoAntecipado", "valorInformado" })
            if (Property(data, name) is JsonElement amount && amount.ValueKind != JsonValueKind.Null &&
                (amount.ValueKind != JsonValueKind.Number || !amount.TryGetDecimal(out var number) || number < 0))
                return "O valor do registro deve ser um numero maior ou igual a zero.";
        var dependencies = entity switch
        {
            "reservas" => new[] { ("quartoId", "quartos", true), ("clienteId", "clientes", true), ("criadoPorUsuarioId", "usuarios", false) },
            "usuarios" => new[] { ("perfilId", "perfis", true) },
            "lancamentos_financeiros" => new[] { ("reservaId", "reservas", false) },
            _ => []
        };
        foreach (var (field, target, required) in dependencies)
        {
            var value = Text(data, field);
            if (!required && string.IsNullOrWhiteSpace(value)) continue;
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty || !await exists(target, id))
                return $"O registro depende de {target} que ainda nao foi sincronizado nesta pousada. Corrija a pendencia e envie novamente.";
        }
        return null;
    }

    private static bool PositiveInteger(JsonElement data, string field) =>
        Property(data, field) is JsonElement value && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number > 0;

    private static JsonElement? Property(JsonElement data, string name)
    {
        foreach (var property in data.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return null;
    }
    private static string Text(JsonElement data, string name) => Property(data, name)?.ToString() ?? "";

    [GeneratedRegex(@"^pbkdf2_sha256\$(?:120000|[6-9][0-9]{5}|1000000)\$[A-Za-z0-9_-]{22}\$[A-Za-z0-9_-]{43}$")]
    private static partial Regex PasswordHash();
}
