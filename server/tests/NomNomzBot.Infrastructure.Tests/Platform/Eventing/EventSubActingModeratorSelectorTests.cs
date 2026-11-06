// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// A channel with no broadcaster token still gets every Twitch topic a moderator may authorize. The system acts as
/// the highest-ranked moderator who holds their own live Twitch connection; onboarding replaces that with the
/// broadcaster. A user who acts in the dashboard is never promoted by this: the pick only decides who authorizes
/// the system's own subscriptions.
/// </summary>
public sealed class EventSubActingModeratorSelectorTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000a1");

    private static AuthDbContext Seed(bool onboarded)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Guid.NewGuid(),
                Name = "kenku",
                NameNormalized = "kenku",
                TwitchChannelId = "170805317",
                IsOnboarded = onboarded,
            }
        );
        db.SaveChanges();
        return db;
    }

    // A moderator with a membership of the given rank and, when linked, a live own Twitch connection.
    private static Guid AddMember(
        AuthDbContext db,
        string twitchUserId,
        ManagementRole role,
        bool linked = true,
        string connectionStatus = AuthEnums.IntegrationStatus.Connected
    )
    {
        Guid userId = Guid.CreateVersion7();
        db.Users.Add(
            new()
            {
                Id = userId,
                TwitchUserId = twitchUserId,
                Username = $"user{twitchUserId}",
                DisplayName = $"User{twitchUserId}",
                UsernameNormalized = $"user{twitchUserId}",
            }
        );
        db.ChannelMemberships.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = userId,
                ManagementRole = role,
                Source = MembershipSource.TwitchBadge,
            }
        );
        if (linked)
            db.IntegrationConnections.Add(
                new()
                {
                    Provider = AuthEnums.IntegrationProvider.Twitch,
                    ProviderAccountId = twitchUserId,
                    Status = connectionStatus,
                    Scopes = ["moderator:read:followers"],
                }
            );
        db.SaveChanges();
        return userId;
    }

    private static EventSubActingModeratorSelector Selector(AuthDbContext db) => new(db);

    [Fact]
    public async Task The_highest_ranked_moderator_with_a_live_connection_is_chosen()
    {
        AuthDbContext db = Seed(onboarded: false);
        AddMember(db, "501", ManagementRole.Moderator);
        Guid lead = AddMember(db, "502", ManagementRole.LeadModerator);
        AddMember(db, "503", ManagementRole.Editor, linked: false);

        EventSubActingModerator? acting = await Selector(db).SelectAsync(Channel);

        acting.Should().NotBeNull();
        acting!.UserId.Should().Be(lead);
        acting.TwitchUserId.Should().Be("502");
        acting.Role.Should().Be(ManagementRole.LeadModerator);
    }

    [Fact]
    public async Task A_moderator_whose_connection_needs_reauth_is_skipped()
    {
        AuthDbContext db = Seed(onboarded: false);
        AddMember(
            db,
            "501",
            ManagementRole.Editor,
            connectionStatus: AuthEnums.IntegrationStatus.NeedsReauth
        );
        Guid moderator = AddMember(db, "502", ManagementRole.Moderator);

        EventSubActingModerator? acting = await Selector(db).SelectAsync(Channel);

        acting!.UserId.Should().Be(moderator);
    }

    [Fact]
    public async Task An_onboarded_channel_has_no_acting_moderator_because_the_broadcaster_authorizes()
    {
        AuthDbContext db = Seed(onboarded: true);
        AddMember(db, "501", ManagementRole.Editor);

        EventSubActingModerator? acting = await Selector(db).SelectAsync(Channel);

        acting.Should().BeNull();
    }

    [Fact]
    public async Task A_channel_with_no_connected_moderator_has_no_acting_moderator()
    {
        AuthDbContext db = Seed(onboarded: false);
        AddMember(db, "501", ManagementRole.Moderator, linked: false);

        EventSubActingModerator? acting = await Selector(db).SelectAsync(Channel);

        acting.Should().BeNull();
    }
}
