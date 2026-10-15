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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The viewer sign-in asks Twitch for identity only and never turns the viewer into a streamer: no channel,
/// no stored Twitch grant, no onboarding sweep. Only the viewer's own User row is written.
/// </summary>
public sealed class AuthServiceViewerLoginTests
{
    [Fact]
    public async Task Viewer_authorize_url_asks_for_openid_only()
    {
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            AuthTestBuilder.NewContext(),
            new RecordingEventBus()
        );

        Result<string> url = await service.GetTwitchViewerOAuthUrl("state-1", "https://x.test");

        url.IsSuccess.Should().BeTrue();
        Uri uri = new(url.Value);
        System.Collections.Specialized.NameValueCollection query =
            System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["scope"].Should().Be("openid");
        query["state"].Should().Be("state-1");
    }

    [Fact]
    public async Task Viewer_login_writes_the_user_but_no_channel_grant_or_onboarding()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(db, bus);

        Result<AuthResultDto> result = await service.HandleTwitchViewerCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );

        result.IsSuccess.Should().BeTrue();
        User user = await db.Users.SingleAsync();
        user.TwitchUserId.Should().Be("tw-100");
        (await db.Channels.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.IntegrationConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        bus.Published.Should().NotContain(e => e is ChannelOnboardedEvent);
    }
}
