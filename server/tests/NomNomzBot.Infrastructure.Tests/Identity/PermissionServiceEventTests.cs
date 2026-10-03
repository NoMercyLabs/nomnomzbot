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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Platform;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves <see cref="PermissionService"/> raises <see cref="PermissionChangedEvent"/> after a grant or revoke
/// actually changed the stored value (allow = 1, deny = 0), and raises nothing for a no-op or a failed save.
/// </summary>
public sealed class PermissionServiceEventTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000002001");
    private const string UserId = "0192a000-0000-7000-8000-000000002002";
    private const string Key = "commands.manage";

    private static (
        PermissionService Sut,
        FeatureServiceTestDbContext Db,
        RecordingEventBus Bus
    ) Build()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        RecordingEventBus bus = new();
        return (new(db, bus), db, bus);
    }

    [Fact]
    public async Task Grant_raises_PermissionChanged_with_value_1_after_the_row_is_stored()
    {
        (PermissionService sut, FeatureServiceTestDbContext db, RecordingEventBus bus) = Build();

        Result result = await sut.GrantAsync(Channel.ToString(), UserId, Key);

        result.IsSuccess.Should().BeTrue();
        Permission stored = await db.Permissions.AsNoTracking().SingleAsync();
        stored.PermissionValue.Should().Be("allow");
        PermissionChangedEvent evt = bus.Published.OfType<PermissionChangedEvent>().Single();
        evt.BroadcasterId.Should().Be(Channel);
        evt.SubjectType.Should().Be("user");
        evt.SubjectId.Should().Be(UserId);
        evt.ResourceType.Should().Be("channel");
        evt.ResourceId.Should().Be(Key);
        evt.NewPermissionValue.Should().Be(1);
    }

    [Fact]
    public async Task Grant_over_an_existing_deny_raises_value_1()
    {
        (PermissionService sut, FeatureServiceTestDbContext db, RecordingEventBus bus) = Build();
        await sut.GrantAsync(Channel.ToString(), UserId, Key);
        await sut.RevokeAsync(Channel.ToString(), UserId, Key);
        bus.Published.Clear();

        await sut.GrantAsync(Channel.ToString(), UserId, Key);

        (await db.Permissions.AsNoTracking().SingleAsync()).PermissionValue.Should().Be("allow");
        bus.Published.OfType<PermissionChangedEvent>().Single().NewPermissionValue.Should().Be(1);
    }

    [Fact]
    public async Task Revoke_raises_PermissionChanged_with_value_0_after_the_row_is_denied()
    {
        (PermissionService sut, FeatureServiceTestDbContext db, RecordingEventBus bus) = Build();
        await sut.GrantAsync(Channel.ToString(), UserId, Key);
        bus.Published.Clear();

        Result result = await sut.RevokeAsync(Channel.ToString(), UserId, Key);

        result.IsSuccess.Should().BeTrue();
        (await db.Permissions.AsNoTracking().SingleAsync()).PermissionValue.Should().Be("deny");
        PermissionChangedEvent evt = bus.Published.OfType<PermissionChangedEvent>().Single();
        evt.BroadcasterId.Should().Be(Channel);
        evt.SubjectType.Should().Be("user");
        evt.SubjectId.Should().Be(UserId);
        evt.ResourceType.Should().Be("channel");
        evt.ResourceId.Should().Be(Key);
        evt.NewPermissionValue.Should().Be(0);
    }

    [Fact]
    public async Task Grant_that_changes_nothing_raises_no_event()
    {
        (PermissionService sut, _, RecordingEventBus bus) = Build();
        await sut.GrantAsync(Channel.ToString(), UserId, Key);
        bus.Published.Clear();

        await sut.GrantAsync(Channel.ToString(), UserId, Key);

        bus.Published.OfType<PermissionChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_with_nothing_stored_or_already_denied_raises_no_event()
    {
        (PermissionService sut, _, RecordingEventBus bus) = Build();

        await sut.RevokeAsync(Channel.ToString(), UserId, Key);
        await sut.GrantAsync(Channel.ToString(), UserId, Key);
        await sut.RevokeAsync(Channel.ToString(), UserId, Key);
        bus.Published.Clear();
        await sut.RevokeAsync(Channel.ToString(), UserId, Key);

        bus.Published.OfType<PermissionChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_whose_save_fails_raises_no_event()
    {
        (PermissionService sut, FeatureServiceTestDbContext db, RecordingEventBus bus) = Build();

        // A null subject id violates the NOT NULL column, so the real SQLite save throws.
        Func<Task> act = async () => await sut.GrantAsync(Channel.ToString(), null!, Key);

        await act.Should().ThrowAsync<DbUpdateException>();
        bus.Published.OfType<PermissionChangedEvent>().Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.Permissions.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
