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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves per-platform channel provisioning (combined-chat item 6): a streamer's YouTube/Kick presence gets its
/// OWN tenant <c>Channel</c> row (Provider + ExternalChannelId, no Twitch id), created once and reused thereafter
/// for the same <c>(provider, externalId)</c>, inheriting the owner's primary-channel deployment mode + tier so
/// it does not silently default to a different profile, and truncating an over-long platform title to the Name cap.
/// </summary>
public sealed class PlatformChannelProvisionerTests
{
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");

    private static void SeedOwnerTwitchChannel(AuthDbContext db, string tier)
    {
        db.Channels.Add(
            new Channel
            {
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                TwitchChannelId = "tw123",
                ExternalChannelId = "tw123",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = tier,
            }
        );
    }

    [Fact]
    public async Task Creates_a_platform_channel_inheriting_the_owner_profile()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SeedOwnerTwitchChannel(db, tier: "pro");
        await db.SaveChangesAsync();
        PlatformChannelProvisioner sut = new(db);

        Guid id = await sut.GetOrCreateAsync(
            Owner,
            AuthEnums.Platform.YouTube,
            "UCyt",
            "My YT Channel"
        );

        id.Should().NotBeEmpty();
        Channel created = await db
            .Channels.IgnoreQueryFilters()
            .SingleAsync(c => c.Provider == AuthEnums.Platform.YouTube);
        created.Id.Should().Be(id);
        created.OwnerUserId.Should().Be(Owner);
        created.ExternalChannelId.Should().Be("UCyt");
        created.Name.Should().Be("My YT Channel");
        created.TwitchChannelId.Should().BeNull();
        created.IsOnboarded.Should().BeTrue();
        // Inherited from the owner's Twitch channel, NOT the "free" fallback — proves the profile is read.
        created.BillingTierKey.Should().Be("pro");
    }

    [Fact]
    public async Task Is_idempotent_returning_the_same_tenant_for_the_same_provider_and_id()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PlatformChannelProvisioner sut = new(db);

        Guid first = await sut.GetOrCreateAsync(Owner, AuthEnums.Platform.YouTube, "UCyt", "My YT");
        Guid second = await sut.GetOrCreateAsync(
            Owner,
            AuthEnums.Platform.YouTube,
            "UCyt",
            "My YT renamed"
        );

        second.Should().Be(first);
        (
            await db
                .Channels.IgnoreQueryFilters()
                .CountAsync(c => c.Provider == AuthEnums.Platform.YouTube)
        )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Truncates_a_display_name_longer_than_the_name_cap()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PlatformChannelProvisioner sut = new(db);
        string longName = new('a', 40);

        await sut.GetOrCreateAsync(Owner, AuthEnums.Platform.YouTube, "UCyt", longName);

        Channel created = await db
            .Channels.IgnoreQueryFilters()
            .SingleAsync(c => c.Provider == AuthEnums.Platform.YouTube);
        created.Name.Length.Should().Be(25);
        created.NameNormalized.Should().Be(created.Name.ToLowerInvariant());
    }
}
