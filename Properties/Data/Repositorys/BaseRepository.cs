using ApiEasyStay.Properties.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiEasyStay.Properties.Data.Repositorys;

public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : BaseEntity
{
    private readonly AppDbContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public BaseRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    public async Task<IReadOnlyList<TEntity>> ListarAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    public async Task<TEntity> AdicionarAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<TEntity> AtualizarAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<bool> RemoverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await ObterPorIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        entity.DataHoraAtualizado = now;
        entity.DataHoraDeletado = now;

        _dbSet.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}