using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Data.Repositorys;

public interface IBaseRepository<TEntity> where TEntity : BaseEntity
{
    Task<IReadOnlyList<TEntity>> ListarAsync(CancellationToken cancellationToken = default);

    Task<TEntity?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TEntity> AdicionarAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<TEntity> AtualizarAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<bool> RemoverAsync(Guid id, CancellationToken cancellationToken = default);
}