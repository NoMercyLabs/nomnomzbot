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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves the feature-flag admin writes (rollout-updates §5): SetFlag creates then updates a flag by key; SetOverride
/// upserts a per-tenant override, invalidates that channel's cached evaluation, and emits the changed + audit events;
/// writes against an unknown flag fail NOT_FOUND.
/// </summary>
public sealed class FeatureFlagAdminServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000008001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (
        FeatureFlagAdminService Sut,
        AuthDbContext Db,
        ICacheService Cache,
        RecordingEventBus Bus
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICacheService cache = Substitute.For<ICacheService>();
        RecordingEventBus bus = new();
        return (new(db, bus, cache, new FakeTimeProvider(Now)), db, cache, bus);
    }

    private static async Task SeedFlagAsync(AuthDbContext db, string key = "feat")
    {
        db.FeatureFlags.Add(
            new()
            {
                Key = key,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetFlag_creates_then_updates_by_key()
    {
        (FeatureFlagAdminService sut, AuthDbContext db, _, _) = Build();

        await sut.SetFlagAsync(new("feat", "desc", true, 50), null);
        await sut.SetFlagAsync(new("feat", "desc", true, 100), null);

        db.FeatureFlags.Single().RolloutPercentage.Should().Be(100);
    }

    [Fact]
    public async Task SetOverride_upserts_invalidates_cache_and_emits_events()
    {
        (
            FeatureFlagAdminService sut,
            AuthDbContext db,
            ICacheService cache,
            RecordingEventBus bus
        ) = Build();
        await SeedFlagAsync(db);

        Result result = await sut.SetOverrideAsync("feat", Channel, new(IsEnabled: true), null);

        result.IsSuccess.Should().BeTrue();
        db.FeatureFlagOverrides.Single().IsEnabled.Should().BeTrue();
        await cache.Received().RemoveAsync($"ff:feat:{Channel}", Arg.Any<CancellationToken>());
        bus.Published.OfType<FeatureFlagChangedEvent>()
            .Should()
            .ContainSingle(e => e.FlagKey == "feat");
        bus.Published.OfType<FeatureFlagAdministeredEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task SetOverride_on_an_unknown_flag_is_not_found()
    {
        (FeatureFlagAdminService sut, _, _, _) = Build();

        Result result = await sut.SetOverrideAsync("missing", Channel, new(IsEnabled: true), null);

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task RemoveOverride_removes_and_invalidates()
    {
        (FeatureFlagAdminService sut, AuthDbContext db, ICacheService cache, _) = Build();
        await SeedFlagAsync(db);
        await sut.SetOverrideAsync("feat", Channel, new(true), null);

        Result result = await sut.RemoveOverrideAsync("feat", Channel, null);

        result.IsSuccess.Should().BeTrue();
        db.FeatureFlagOverrides.Should().BeEmpty();
        await cache.Received(2).RemoveAsync($"ff:feat:{Channel}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFlag_writes_an_audit_row_naming_the_actor_flag_old_and_new_value()
    {
        (FeatureFlagAdminService sut, AuthDbContext db, _, _) = Build();
        Guid actor = Guid.Parse("0192a000-0000-7000-8000-0000000090aa");

        await sut.SetFlagAsync(new("feat", "desc", false, 0), actor);
        await sut.SetFlagAsync(new("feat", "desc", true, 100), actor);

        db.IamAuditLogs.Should().HaveCount(2);
        NomNomzBot.Domain.Identity.Entities.IamAuditLog second = db
            .IamAuditLogs.OrderBy(a => a.Id)
            .Last();
        second.TargetResource.Should().Be("feat");
        second.Justification.Should().Contain(actor.ToString());
        second.Justification.Should().Contain("enabled=False");
        second.Justification.Should().Contain("enabled=True");
    }

    [Fact]
    public async Task SetOverride_and_RemoveOverride_each_write_an_audit_row()
    {
        (FeatureFlagAdminService sut, AuthDbContext db, _, _) = Build();
        await SeedFlagAsync(db);
        Guid actor = Guid.Parse("0192a000-0000-7000-8000-0000000090bb");

        await sut.SetOverrideAsync("feat", Channel, new(true), actor);
        await sut.RemoveOverrideAsync("feat", Channel, actor);

        db.IamAuditLogs.Should().HaveCount(2);
        db.IamAuditLogs.Should()
            .Contain(a => a.Justification != null && a.Justification.Contains("new=(removed)"));
    }
}
