using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public sealed class EspacoSincronizacaoEntity : BaseEntity
{
    public string ChaveHash { get; set; } = string.Empty;

    public string? Nome { get; set; }

    public bool Ativo { get; set; } = true;
}
