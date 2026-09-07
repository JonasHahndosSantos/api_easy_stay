using ApiEasyStay.Properties.Data.Repositorys;
using ApiEasyStay.Properties.Dtos;
using ApiEasyStay.Properties.Entities;
using Mapster;

namespace ApiEasyStay.Properties.Services;

public class BaseService<TEntity, TDto> : IBaseService<TEntity, TDto>
    where TEntity : BaseEntity, new()
    where TDto : BaseDto
{
    private readonly IBaseRepository<TEntity> _repository;

    public BaseService(IBaseRepository<TEntity> repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<TDto>> ListarAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _repository.ListarAsync(cancellationToken);
        return entities.Adapt<List<TDto>>();
    }

    public async Task<TDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.ObterPorIdAsync(id, cancellationToken);
        return entity is null ? null : entity.Adapt<TDto>();
    }

    public async Task<TDto> AdicionarAsync(TDto dto, CancellationToken cancellationToken = default)
    {
        var entity = dto.Adapt<TEntity>();
        var now = DateTime.UtcNow;

        entity.Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
        entity.DataHoraCriado = now;
        entity.DataHoraAtualizado = now;
        entity.DataHoraDeletado = null;

        var created = await _repository.AdicionarAsync(entity, cancellationToken);
        return created.Adapt<TDto>();
    }

    public async Task<TDto?> AtualizarAsync(Guid id, TDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.ObterPorIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var dataHoraCriado = entity.DataHoraCriado;
        var dataHoraDeletado = entity.DataHoraDeletado;

        dto.Id = id;
        dto.Adapt(entity);

        entity.Id = id;
        entity.DataHoraCriado = dataHoraCriado;
        entity.DataHoraAtualizado = DateTime.UtcNow;
        entity.DataHoraDeletado = dataHoraDeletado;

        var updated = await _repository.AtualizarAsync(entity, cancellationToken);
        return updated.Adapt<TDto>();
    }

    public Task<bool> RemoverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _repository.RemoverAsync(id, cancellationToken);
    }
}