using ApiEasyStay.Properties.Dtos;
using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Services;

public interface IBaseService<TEntity, TDto>
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    Task<IReadOnlyList<TDto>> ListarAsync(CancellationToken cancellationToken = default);

    Task<TDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TDto> AdicionarAsync(TDto dto, CancellationToken cancellationToken = default);

    Task<TDto?> AtualizarAsync(Guid id, TDto dto, CancellationToken cancellationToken = default);

    Task<bool> RemoverAsync(Guid id, CancellationToken cancellationToken = default);
}