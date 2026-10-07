using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NeuroVox.Application.Repositories;
using NeuroVox.Domain.Entities.Common;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.Persistence.Repositories
{
    public class WriteRepository<T> : IWriteRepository<T> where T : BaseEntity
    {
        private readonly NeuroVoxDbContext _context;

        public WriteRepository(NeuroVoxDbContext context) => _context = context;

        public DbSet<T> Table => _context.Set<T>();

        public async Task<bool> AddAsync(T model)
        {
            EntityEntry<T> entityEntry = await Table.AddAsync(model);
            return entityEntry.State == EntityState.Added;
        }

        public async Task<bool> AddRangeAsync(List<T> datas)
        {
            await Table.AddRangeAsync(datas);
            return true;
        }

        public bool Remove(T model)
        {
            model.RowIsActive = false;
            return true;
        }

        public bool RemoveRange(List<T> datas)
        {
            datas.ForEach(d => d.RowIsActive = false);
            return true;
        }

        public async Task<bool> RemoveAsync(string id)
        {
            if (!Guid.TryParse(id, out var guidId))
                throw new ArgumentException($"Invalid ID format: {id}");

            T? model = await Table.FirstOrDefaultAsync(data => data.Id == guidId);
            if (model is null)
                throw new KeyNotFoundException($"{typeof(T).Name} not found: {id}");

            return Remove(model);
        }

        public bool Update(T model)
        {
            EntityEntry<T> entityEntry = Table.Update(model);
            return entityEntry.State == EntityState.Modified;
        }

        public Task<int> SaveAsync() => _context.SaveChangesAsync();
    }
}
