using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

/// <summary>
/// Current materialized state for one record in a synchronized workspace.
/// The event journal remains immutable in sincronizacoes; this table makes
/// the current cloud data directly inspectable and isolated per workspace.
/// </summary>
public sealed class SyncEntityStateEntity : BaseEntity
{
    public Guid EspacoId { get; set; }

    public string Entidade { get; set; } = string.Empty;

    public Guid EntidadeId { get; set; }

    public string Dados { get; set; } = "{}";

    public bool Excluido { get; set; }

    public DateTime DataHoraOrigem { get; set; }
}
