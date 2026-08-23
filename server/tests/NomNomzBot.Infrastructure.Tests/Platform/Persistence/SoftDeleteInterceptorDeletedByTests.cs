// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S013d: every <see cref="NomNomzBot.Domain.Platform.SoftDeletableEntity"/> row must record WHO deleted
/// it, not just when. Before this slice <c>DeletedBy</c> did not exist at all — 84 soft-deletable entity
/// types across the codebase carried an unattributable <c>DeletedAt</c>. Proven across THREE unrelated
/// entity types (<see cref="ChatFilter"/> — moderation, <see cref="VtsConnection"/> — integrations,
/// <see cref="PermitGrant"/> — identity/roles) to show the fix is the shared
/// <see cref="SoftDeleteInterceptor"/> seam, not a per-entity patch. Runs on the real relational SQLite
/// <see cref="EventStoreTestDbContext"/> harness with the production interceptor wired in.
/// </summary>
public sealed class SoftDeleteInterceptorDeletedByTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Open();

    public void Dispose() => _database.Dispose();

    private EventStoreTestDbContext NewContext(
        FakeTimeProvider time,
        StubCurrentUserService user
    ) => _database.NewContext([new SoftDeleteInterceptor(time, user)]);

    [Fact]
    public async Task DirectPropertyAssignment_StampsActingUser_ForChatFilter()
    {
        Guid actor = Guid.NewGuid();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T10:00:00Z"));
        StubCurrentUserService user = StubCurrentUserService.For(actor);

        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            db.ChatFilters.Add(
                new()
                {
                    BroadcasterId = Channel,
                    FilterType = ChatFilterType.Blocklist,
                    Name = "test-filter",
                    Action = ChatFilterAction.Delete,
                }
            );
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            ChatFilter filter = await db.ChatFilters.SingleAsync(f => f.BroadcasterId == Channel);
            // The service-layer shape: load, flip DeletedAt directly, save — no Remove() call.
            filter.DeletedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext verify = NewContext(time, user))
        {
            ChatFilter deleted = await verify
                .ChatFilters.IgnoreQueryFilters()
                .SingleAsync(f => f.BroadcasterId == Channel);
            deleted.DeletedAt.Should().NotBeNull();
            deleted.DeletedBy.Should().Be(actor);
        }
    }

    [Fact]
    public async Task RemoveCall_ConvertedToSoftDelete_StampsActingUser_ForVtsConnection()
    {
        Guid actor = Guid.NewGuid();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T10:00:00Z"));
        StubCurrentUserService user = StubCurrentUserService.For(actor);

        Guid id;
        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            VtsConnection connection = new() { BroadcasterId = Channel };
            db.VtsConnections.Add(connection);
            await db.SaveChangesAsync();
            id = connection.Id;
        }

        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            VtsConnection connection = await db.VtsConnections.SingleAsync(c => c.Id == id);
            db.VtsConnections.Remove(connection); // SoftDeleteInterceptor converts this to a soft delete
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext verify = NewContext(time, user))
        {
            VtsConnection deleted = await verify
                .VtsConnections.IgnoreQueryFilters()
                .SingleAsync(c => c.Id == id);
            deleted.DeletedAt.Should().NotBeNull();
            deleted.DeletedBy.Should().Be(actor);
        }
    }

    [Fact]
    public async Task ImpersonatedDelete_RecordsTheOperator_NotTheSubject_ForPermitGrant()
    {
        Guid operatorUserId = Guid.NewGuid();
        Guid subjectUserId = Guid.NewGuid();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T10:00:00Z"));
        StubCurrentUserService impersonating = StubCurrentUserService.Impersonating(
            operatorUserId,
            subjectUserId,
            Guid.NewGuid()
        );

        Guid grantId;
        using (EventStoreTestDbContext db = NewContext(time, impersonating))
        {
            PermitGrant grant = new()
            {
                BroadcasterId = Channel,
                UserId = Guid.NewGuid(),
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Moderator,
                GrantedByUserId = operatorUserId,
            };
            db.PermitGrants.Add(grant);
            await db.SaveChangesAsync();
            grantId = grant.Id;
        }

        // The request runs as an act-as session for `subjectUserId` — its own JWT `sub` IS the
        // subject — but the delete must attribute the OPERATOR who is actually driving the session.
        using (EventStoreTestDbContext db = NewContext(time, impersonating))
        {
            PermitGrant grant = await db.PermitGrants.SingleAsync(g => g.Id == grantId);
            grant.DeletedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext verify = NewContext(time, impersonating))
        {
            PermitGrant deleted = await verify
                .PermitGrants.IgnoreQueryFilters()
                .SingleAsync(g => g.Id == grantId);
            deleted.DeletedBy.Should().Be(operatorUserId);
            deleted.DeletedBy.Should().NotBe(subjectUserId);
        }
    }

    [Fact]
    public async Task Restoring_ClearsDeletedBy()
    {
        Guid actor = Guid.NewGuid();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T10:00:00Z"));
        StubCurrentUserService user = StubCurrentUserService.For(actor);

        Guid id;
        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            ChatFilter filter = new()
            {
                BroadcasterId = Channel,
                FilterType = ChatFilterType.Blocklist,
                Name = "restorable",
                Action = ChatFilterAction.Delete,
            };
            db.ChatFilters.Add(filter);
            await db.SaveChangesAsync();
            id = filter.Id;
        }

        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            ChatFilter filter = await db.ChatFilters.SingleAsync(f => f.Id == id);
            filter.DeletedAt = time.GetUtcNow().UtcDateTime; // delete
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext db = NewContext(time, user))
        {
            ChatFilter filter = await db
                .ChatFilters.IgnoreQueryFilters()
                .SingleAsync(f => f.Id == id);
            filter.DeletedBy.Should().Be(actor); // sanity: the delete got stamped before the restore

            filter.DeletedAt = null; // restore
            await db.SaveChangesAsync();
        }

        using (EventStoreTestDbContext verify = NewContext(time, user))
        {
            ChatFilter restored = await verify.ChatFilters.SingleAsync(f => f.Id == id);
            restored.DeletedAt.Should().BeNull();
            restored.DeletedBy.Should().BeNull();
        }
    }
}
