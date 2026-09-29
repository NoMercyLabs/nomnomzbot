// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The EventSub inbox on the shared database (twitch-eventsub §10.1). The same database already holds the
/// chat-ingest lease and the journal, so the queue, its single consumer and the dedupe all agree on one
/// source of truth — no second system can disagree with the lease about who processes.
/// A singleton; every call runs in its own short scope.
/// <para>
/// The table is infrastructure-internal (only this class touches it), so it is reached through the
/// context's <c>Set&lt;T&gt;()</c> rather than widening <see cref="IApplicationDbContext"/> for every
/// feature's test double.
/// </para>
/// </summary>
public sealed class DatabaseEventSubInbox(IServiceScopeFactory scopeFactory) : IEventSubInbox
{
    public async Task<bool> EnqueueAsync(
        EventSubInboxMessage message,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db = Resolve(scope);
        DbSet<EventSubInboxMessage> inbox = Messages(db);

        if (await inbox.AnyAsync(m => m.MessageId == message.MessageId, ct))
            return false;

        await inbox.AddAsync(message, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // The other shard stored the same resent notification between the check and the insert.
            if (await IsStoredAsync(message.MessageId, ct))
                return false;
            throw;
        }
    }

    public async Task<IReadOnlyList<EventSubInboxMessage>> PeekAsync(
        int max,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await Messages(Resolve(scope))
            .AsNoTracking()
            .OrderBy(m => m.ReceivedAt)
            .ThenBy(m => m.Id)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db = Resolve(scope);
        DbSet<EventSubInboxMessage> inbox = Messages(db);
        EventSubInboxMessage? row = await inbox.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
            return;
        inbox.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsStoredAsync(string messageId, CancellationToken ct)
    {
        // A fresh scope: the failed insert leaves the first context holding the rejected entity.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await Messages(Resolve(scope)).AnyAsync(m => m.MessageId == messageId, ct);
    }

    private static IApplicationDbContext Resolve(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

    private static DbSet<EventSubInboxMessage> Messages(IApplicationDbContext db) =>
        db is DbContext context
            ? context.Set<EventSubInboxMessage>()
            : throw new InvalidOperationException(
                "The EventSub inbox needs an EF Core DbContext behind IApplicationDbContext."
            );
}
