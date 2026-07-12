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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the uncached DB read behind <see cref="IHubUserEnricher"/> (GAP E3-2): the pronoun display string
/// reuses the exact <c>{{user.pronouns}}</c> formatting (<c>UserPronounDisplay</c>), the community standing is
/// read only when a row exists for this exact (broadcaster, viewer) pair, and a viewer with no internal
/// <c>User</c> row at all resolves to <c>null</c> rather than a partially-populated result.
/// </summary>
public sealed class HubUserEnrichmentStoreTests
{
    [Fact]
    public async Task Known_viewer_with_alt_pronoun_and_standing_returns_every_field()
    {
        HubUserEnrichmentTestDbContext db = HubUserEnrichmentTestDbContext.New();
        Guid broadcasterId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        Pronoun theyThem = new()
        {
            Key = "theythem",
            Name = "they/them",
            Subject = "they",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
        };
        Pronoun sheHer = new()
        {
            Key = "sheher",
            Name = "she/her",
            Subject = "she",
            Object = "her",
            Possessive = "her",
            GenderedTerm = "gal",
            Singular = true,
        };
        db.Pronouns.AddRange(theyThem, sheHer);
        await db.SaveChangesAsync();

        db.Users.Add(
            new User
            {
                Id = userId,
                TwitchUserId = "u1",
                Username = "stoney_eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney",
                ProfileImageUrl = "https://cdn/avatar.png",
                PronounId = theyThem.Id,
                AltPronounId = sheHer.Id,
            }
        );
        db.ChannelCommunityStandings.Add(
            new ChannelCommunityStanding
            {
                BroadcasterId = broadcasterId,
                UserId = userId,
                Standing = CommunityStanding.Vip,
                LevelValue = CommunityStanding.Vip.ToLevel(),
                Source = StandingSource.EventSubBadge,
            }
        );
        await db.SaveChangesAsync();

        HubUserEnrichmentStore store = new(db);
        HubUserEnrichment? result = await store.LoadAsync(broadcasterId, "u1");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Stoney");
        result.AvatarUrl.Should().Be("https://cdn/avatar.png");
        result
            .Pronouns.Should()
            .Be("they/she", "an alt pronoun renders as a subject/subject badge");
        result.CommunityStanding.Should().Be("Vip");
    }

    [Fact]
    public async Task Viewer_with_no_recorded_standing_in_this_channel_returns_null_standing_not_everyone()
    {
        HubUserEnrichmentTestDbContext db = HubUserEnrichmentTestDbContext.New();
        Guid userId = Guid.CreateVersion7();
        db.Users.Add(
            new User
            {
                Id = userId,
                TwitchUserId = "u2",
                Username = "viewer2",
                UsernameNormalized = "viewer2",
                DisplayName = "Viewer2",
            }
        );
        await db.SaveChangesAsync();

        HubUserEnrichmentStore store = new(db);
        HubUserEnrichment? result = await store.LoadAsync(Guid.CreateVersion7(), "u2");

        result.Should().NotBeNull();
        result!
            .CommunityStanding.Should()
            .BeNull("no ChannelCommunityStanding row exists for this pair");
        result.Pronouns.Should().BeNull("the viewer has no primary pronoun resolved yet");
    }

    [Fact]
    public async Task Unknown_twitch_user_id_returns_null()
    {
        HubUserEnrichmentTestDbContext db = HubUserEnrichmentTestDbContext.New();
        HubUserEnrichmentStore store = new(db);

        HubUserEnrichment? result = await store.LoadAsync(Guid.CreateVersion7(), "never-seen");

        result.Should().BeNull();
    }
}
