// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Sign-in by role: each role asks Twitch for only the scopes that role uses. A viewer asks identity only; a
/// moderator asks the scopes the dashboard signs with the operator's own token; a broadcaster asks the base
/// channel grant. A viewer or moderator start never widens to a held broadcaster grant, even with a hint.
/// </summary>
public sealed class AuthServiceRoleLoginScopesTests
{
    private static readonly string[] ModeratorScopes =
    [
        "user:read:moderated_channels",
        "user:write:chat",
        "user:read:emotes",
        "moderator:manage:banned_users",
        "moderator:read:unban_requests",
        "moderator:manage:unban_requests",
        "moderator:read:blocked_terms",
        "moderator:manage:blocked_terms",
        "moderator:manage:chat_messages",
        "moderator:read:shield_mode",
        "moderator:manage:shield_mode",
        "moderator:manage:warnings",
        "moderator:manage:suspicious_users",
    ];

    [Fact]
    public async Task Viewer_start_asks_identity_and_the_chat_composer_scopes_only()
    {
        string[] requested = await RequestedScopesAsync(TwitchLoginRole.Viewer, null);

        requested.Should().BeEquivalentTo("user:read:chat", "user:write:chat", "user:read:emotes");
        requested.Should().NotContain(s => s.StartsWith("moderator:") || s.StartsWith("channel:"));
    }

    [Fact]
    public async Task Moderator_start_asks_the_moderation_set_and_no_channel_owner_scope()
    {
        string[] requested = await RequestedScopesAsync(TwitchLoginRole.Moderator, null);

        requested.Should().BeEquivalentTo(ModeratorScopes);
        requested
            .Should()
            .NotContain(["channel:manage:raids", "user:bot", "channel:manage:broadcast"]);
    }

    [Fact]
    public async Task Broadcaster_start_asks_the_base_channel_grant()
    {
        string[] requested = await RequestedScopesAsync(TwitchLoginRole.Broadcaster, null);

        requested
            .Should()
            .BeEquivalentTo("user:read:chat", "user:write:chat", "user:read:moderated_channels");
    }

    [Fact]
    public async Task Moderator_start_with_a_held_broadcaster_grant_does_not_widen_to_it()
    {
        Guid channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = channel,
                Provider = "twitch",
                Status = "connected",
                Scopes = ["user:bot", "channel:manage:raids"],
            }
        );
        await db.SaveChangesAsync();

        string[] requested = await RequestedScopesAsync(TwitchLoginRole.Moderator, channel, db);

        requested.Should().BeEquivalentTo(ModeratorScopes);
    }

    private static async Task<string[]> RequestedScopesAsync(
        TwitchLoginRole role,
        Guid? hint,
        AuthDbContext? db = null
    )
    {
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db ?? AuthTestBuilder.NewContext(),
            new RecordingEventBus()
        );

        Result<string> url = await service.GetTwitchOAuthUrl(
            role,
            "state-1",
            "https://x.test",
            hint
        );

        url.IsSuccess.Should().BeTrue();
        NameValueCollection query = HttpUtility.ParseQueryString(new Uri(url.Value).Query);
        return query["scope"]!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
