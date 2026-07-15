using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class ReservaEntity : BaseEntity
{
    public Guid QuartoId { get; set; }

    public QuartoEntity? Quarto { get; set; }

    public Guid ClienteId { get; set; }

    public ClienteEntity? Cliente { get; set; }

    public Guid CriadoPorUsuarioId { get; set; }

    public UsuarioEntity? CriadoPorUsuario { get; set; }

    public DateTime DataEntrada { get; set; }

    public DateTime DataSaida { get; set; }

    public int QuantidadeHospedes { get; set; }

    public decimal ValorTotal { get; set; }

    public int Status { get; set; }

    public string? Observacoes { get; set; }
}
