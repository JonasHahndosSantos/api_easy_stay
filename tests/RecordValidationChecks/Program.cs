using System.Text.Json;
using ApiEasyStay.Properties.Services;

static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value);
var roomId = Guid.NewGuid();
var customerId = Guid.NewGuid();
var room = Data(new { nome = "Suite", capacidade = 2, status = 0, precoDiaria = 100 });
Dictionary<string, object> Reservation() => new() {
    ["quartoId"] = roomId, ["clienteId"] = customerId,
    ["dataEntrada"] = "2030-01-01T00:00:00", ["dataSaida"] = "2030-01-03T00:00:00",
    ["status"] = 1, ["quantidadeHóspedes"] = 2, ["valorTotal"] = 100
};
async Task Check(Dictionary<string, object> values, bool valid, bool exists = true) {
    var error = await SyncRecordValidator.ValidateAsync("reservas", Data(values),
        (_, _) => Task.FromResult(exists), _ => Task.FromResult<JsonElement?>(room));
    if ((error is null) != valid) throw new Exception("Resultado incorreto: " + error);
}
await Check(Reservation(), true);
foreach (var guests in new[] { -2, 0, 3 }) {
    var data = Reservation(); data["quantidadeHóspedes"] = guests; await Check(data, false);
}
var negative = Reservation(); negative["valorPagoAntecipado"] = -1; await Check(negative, false);
await Check(Reservation(), false, exists: false);
room = Data(new { nome = "Suite", capacidade = 1, status = 2, precoDiaria = 100 });
await Check(Reservation(), false);
var cancelled = Reservation(); cancelled["status"] = 4; await Check(cancelled, true);
foreach (var invalid in new[] { Data(new { nome = "Suite", capacidade = -5, status = 0 }),
    Data(new { nome = "Suite", capacidade = 2, status = 99 }), Data(new { nome = "Suite", capacidade = 2, status = "wrong" }) }) {
    if (await SyncRecordValidator.ValidateAsync("quartos", invalid, (_, _) => Task.FromResult(true)) is null)
        throw new Exception("Quarto invalido aceito");
}
Console.WriteLine("OK: validacao de hospedes, capacidade, manutencao, cancelamento, adiantamento, dependencias e status.");
