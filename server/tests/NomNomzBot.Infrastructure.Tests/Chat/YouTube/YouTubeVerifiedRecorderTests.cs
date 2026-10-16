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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// The verified flag YouTube sends on each live chat line (<c>authorDetails.isVerified</c>) is stored on the
/// chatter's user row, so the trust score can read it later. Each line refreshes it, both ways.
/// </summary>
public class YouTubeVerifiedRecorderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199b000-0000-7000-8000-0000000000c1");

    private static (YouTubeVerifiedRecorder Sut, IApplicationDbContext Db) Build()
    {
        IApplicationDbContext db = AuthTestBuilder.NewContext();
        ServiceCollection services = new();
        services.AddSingleton(db);
        services.AddSingleton<ICurrentUserService>(Substitute.For<ICurrentUserService>());
        services.AddSingleton<IUserService>(sp =>
            AuthTestBuilder.UserService(
                db,
                sp.GetRequiredService<ICurrentUserService>(),
                sp.GetRequiredService<IServiceScopeFactory>()
            )
        );
        ServiceProvider provider = services.BuildServiceProvider();
        return (new(provider.GetRequiredService<IServiceScopeFactory>()), db);
    }

    private static ChatMessageReceivedEvent Line(string provider, bool verified) =>
        new()
        {
            BroadcasterId = Broadcaster,
            Provider = provider,
            OccurredAt = DateTimeOffset.UtcNow,
            MessageId = Guid.NewGuid().ToString(),
            TwitchBroadcasterId = "UCstreamer",
            UserId = "UCviewer",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "hi",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
            IsYouTubeChannelVerified = verified,
        };

    [Fact]
    public async Task A_verified_YouTube_line_stores_true_on_the_user()
    {
        (YouTubeVerifiedRecorder sut, IApplicationDbContext db) = Build();

        await sut.HandleAsync(Line(AuthEnums.Platform.YouTube, verified: true));

        User user = await db.Users.SingleAsync();
        user.IsYouTubeChannelVerified.Should().BeTrue();
    }

    [Fact]
    public async Task A_later_unverified_line_clears_the_flag()
    {
        (YouTubeVerifiedRecorder sut, IApplicationDbContext db) = Build();
        await sut.HandleAsync(Line(AuthEnums.Platform.YouTube, verified: true));

        await sut.HandleAsync(Line(AuthEnums.Platform.YouTube, verified: false));

        (await db.Users.SingleAsync()).IsYouTubeChannelVerified.Should().BeFalse();
    }

    [Fact]
    public async Task A_non_YouTube_line_creates_and_changes_nothing()
    {
        (YouTubeVerifiedRecorder sut, IApplicationDbContext db) = Build();

        await sut.HandleAsync(Line(AuthEnums.Platform.Twitch, verified: true));

        (await db.Users.CountAsync()).Should().Be(0);
    }
}
