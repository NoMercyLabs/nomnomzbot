// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

public abstract class GenericRepository<T>
    where T : class
{
    protected readonly AppDbContext Db;
    protected readonly DbSet<T> Set;

    protected GenericRepository(AppDbContext db)
    {
        Db = db;
        Set = db.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken ct = default) =>
        await Set.FindAsync([id], ct);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await Set.ToListAsync(ct);

    public virtual async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    ) => await Set.Where(predicate).ToListAsync(ct);

    public virtual async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    ) => await Set.FirstOrDefaultAsync(predicate, ct);

    public virtual async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    ) => await Set.AnyAsync(predicate, ct);

    public virtual async Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default
    ) => predicate == null ? await Set.CountAsync(ct) : await Set.CountAsync(predicate, ct);

    public virtual async Task AddAsync(T entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void Remove(T entity) => Set.Remove(entity);

    public virtual async Task<PagedList<T>> GetPagedAsync(
        PaginationParams paging,
        Expression<Func<T, bool>>? filter = null,
        CancellationToken ct = default
    )
    {
        IQueryable<T> query = filter != null ? Set.Where(filter) : Set.AsQueryable();
        int total = await query.CountAsync(ct);
        List<T> items = await query
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(ct);
        return new(items, total, paging.Page, paging.PageSize);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await Db.SaveChangesAsync(ct);
}
