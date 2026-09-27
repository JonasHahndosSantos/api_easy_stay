using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public sealed class SyncAccessTokenEntity : BaseEntity
{
    public Guid EspacoId { get; set; }

    public EspacoSincronizacaoEntity? Espaco { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public Guid? DispositivoId { get; set; }

    public string? NomeDispositivo { get; set; }

    public bool Ativo { get; set; } = true;
}
