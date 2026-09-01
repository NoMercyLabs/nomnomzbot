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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S019b — proves the provisioner wiring: a brand-new streamer's first Twitch login
/// (<see cref="AuthService.HandleTwitchCallbackAsync"/>) creates exactly one
/// <see cref="PlatformConnection"/> row for the new <c>Channel</c>, marked primary. This is the
/// CREATE-path slice only — attaching a second platform / folding existing sibling channels is
/// separate future work (S019c/d), not exercised here.
/// </summary>
public sealed class AuthServiceProvisionsPlatformConnectionTests
{
    // Must match AuthServiceReAuthOnboardingRepublishTests.Build's hardcoded mock Twitch user id.
    private const string TwitchUserId = "tw-100";

    [Fact]
    public async Task First_login_creates_exactly_one_primary_PlatformConnection_for_the_new_channel()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(db, bus);

        OAuthCallbackDto callback = new() { Code = "auth-code" };
        AuthContextDto context = new("web", "127.0.0.1", "test-agent");

        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(callback, context);

        result.IsSuccess.Should().BeTrue();

        Channel channel = await db.Channels.SingleAsync(c => c.TwitchChannelId == TwitchUserId);
        Guid channelId = channel.Id;

        List<PlatformConnection> connections = await db
            .PlatformConnections.Where(p => p.ChannelId == channelId)
            .ToListAsync();

        connections.Should().HaveCount(1);
        connections[0].IsPrimary.Should().BeTrue();
        connections[0].Provider.Should().Be(AuthEnums.Platform.Twitch);
        connections[0].ExternalChannelId.Should().Be(TwitchUserId);
    }
}
