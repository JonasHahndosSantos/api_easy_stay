using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public sealed class SyncAuthCodeEntity : BaseEntity
{
    public string IdentificadorHash { get; set; } = string.Empty;

    public string CodigoHash { get; set; } = string.Empty;

    public DateTime ExpiraEm { get; set; }

    public DateTime? UsadoEm { get; set; }

    public int Tentativas { get; set; }

    public string? NomeDispositivo { get; set; }
}
