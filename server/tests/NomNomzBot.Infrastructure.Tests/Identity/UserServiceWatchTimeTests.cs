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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

public sealed class UserServiceWatchTimeTests
{
    [Fact]
    public async Task Channel_appearance_shows_watch_time_for_a_viewer_without_chat_messages()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid userId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = userId,
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "Viewer",
                TwitchUserId = "viewer-1",
                BroadcasterType = "viewer",
                Enabled = true,
            }
        );
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = userId,
                TwitchChannelId = "channel-1",
                ExternalChannelId = "channel-1",
                Name = "channel",
                NameNormalized = "channel",
            }
        );
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = channelId,
                ViewerUserId = userId,
                ViewerTwitchUserId = "viewer-1",
                TotalWatchSeconds = 12_000,
            }
        );
        await db.SaveChangesAsync();

        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IUserService sut = AuthTestBuilder.UserService(
            db,
            currentUser,
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        Result<List<UserChannelAppearanceDto>> result = await sut.GetUserChannelsAsync(
            userId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.WatchTime.Should().Be("3h 20m");
    }

    [Fact]
    public async Task Stats_show_persisted_watch_seconds_as_fractional_hours()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid userId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = userId,
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "Viewer",
                TwitchUserId = "viewer-1",
                BroadcasterType = "viewer",
                Enabled = true,
            }
        );
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = channelId,
                ViewerUserId = userId,
                ViewerTwitchUserId = "viewer-1",
                TotalWatchSeconds = 12_000,
            }
        );
        await db.SaveChangesAsync();

        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IUserService sut = AuthTestBuilder.UserService(
            db,
            currentUser,
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        Result<UserStatsDto> result = await sut.GetStatsAsync(userId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.WatchHours.Should().BeApproximately(3.3333333333333335, 0.000001);
    }
}
