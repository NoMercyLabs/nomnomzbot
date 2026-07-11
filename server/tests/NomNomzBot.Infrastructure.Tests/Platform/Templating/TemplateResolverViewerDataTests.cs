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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;
using InfraViewerDataService = NomNomzBot.Infrastructure.ViewerData.ViewerDataService;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the new template groups resolve real stored state (per-viewer-data.md D3):
/// <c>{viewer.data.*}</c> for the triggering viewer — identity-first for non-Twitch chatters —
/// <c>{target.data.*}</c> for the @mention, <c>{count.*}</c> for the G.4 counters (unset renders 0),
/// the M.1 stat helpers, and that caller seeds always win over auto-resolution.
/// </summary>
public sealed class TemplateResolverViewerDataTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000c001");
    private static readonly Guid Alice = Guid.Parse("0192b400-0000-7000-8000-00000000a001");
    private static readonly Guid Bob = Guid.Parse("0192b400-0000-7000-8000-00000000a002");
    private static readonly Guid YtViewer = Guid.Parse("0192b400-0000-7000-8000-00000000a003");

    private readonly ViewerDataTestDbContext _db;
    private readonly IViewerAnalyticsService _analytics;
    private readonly TemplateResolver _resolver;

    public TemplateResolverViewerDataTests()
    {
        _db = ViewerDataTestDbContext.New();
        _db.Users.Add(
            new User
            {
                Id = Alice,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        _db.Users.Add(
            new User
            {
                Id = Bob,
                TwitchUserId = "222",
                Username = "bob",
                UsernameNormalized = "bob",
                DisplayName = "Bob",
            }
        );
        _db.Users.Add(
            new User
            {
                Id = YtViewer,
                TwitchUserId = null,
                Username = "ytcarol",
                UsernameNormalized = "ytcarol",
                DisplayName = "Carol",
            }
        );
        _db.UserIdentities.Add(
            new UserIdentity
            {
                Id = Guid.NewGuid(),
                UserId = YtViewer,
                Provider = "youtube",
                ProviderUserId = "yt-555",
                ProviderUsername = "ytcarol",
            }
        );
        _db.ViewerData.AddRange(
            new ViewerDatum
            {
                BroadcasterId = Channel,
                ViewerUserId = Alice,
                Key = "deaths",
                Value = "12",
            },
            new ViewerDatum
            {
                BroadcasterId = Channel,
                ViewerUserId = Bob,
                Key = "deaths",
                Value = "99",
            },
            new ViewerDatum
            {
                BroadcasterId = Channel,
                ViewerUserId = YtViewer,
                Key = "quest",
                Value = "started",
            }
        );
        _db.NamedCounters.Add(
            new NamedCounter
            {
                BroadcasterId = Channel,
                Key = "wins",
                Value = 7,
            }
        );
        _db.SaveChanges();

        _analytics = Substitute.For<IViewerAnalyticsService>();
        _analytics
            .GetProfileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ViewerProfileDto>("never seen", "NOT_FOUND"));

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton<IViewerDataService>(
            new InfraViewerDataService(_db, TimeProvider.System)
        );
        services.AddSingleton<INamedCounterService>(new NamedCounterService(_db));
        services.AddSingleton(_analytics);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Dictionary<string, string> TwitchSeeds(
        string userId = "111",
        string? target = null
    )
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = userId,
            ["user.provider"] = "twitch",
        };
        if (target is not null)
            seeds["target"] = target;
        return seeds;
    }

    [Fact]
    public async Task ViewerData_ResolvesTheTriggeringViewersStoredValue()
    {
        string resolved = await _resolver.ResolveAsync(
            "You died {viewer.data.deaths} times.",
            TwitchSeeds(),
            Channel
        );

        resolved.Should().Be("You died 12 times.");
    }

    [Fact]
    public async Task ViewerData_ResolvesIdentityFirst_ForANonTwitchChatter()
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = "yt-555",
            ["user.provider"] = "youtube",
        };

        string resolved = await _resolver.ResolveAsync(
            "Quest: {viewer.data.quest}",
            seeds,
            Channel
        );

        resolved.Should().Be("Quest: started");
    }

    [Fact]
    public async Task ViewerData_UnsetKeyRendersEmpty()
    {
        string resolved = await _resolver.ResolveAsync(
            "[{viewer.data.never_set}]",
            TwitchSeeds(),
            Channel
        );

        resolved.Should().Be("[]");
    }

    [Fact]
    public async Task TargetData_ResolvesTheMentionedViewersValue_NotTheCallers()
    {
        string resolved = await _resolver.ResolveAsync(
            "{viewer.data.deaths} vs {target.data.deaths}",
            TwitchSeeds(target: "Bob"),
            Channel
        );

        resolved.Should().Be("12 vs 99");
    }

    [Fact]
    public async Task Count_ResolvesStoredCounters_AndUnsetRendersZero()
    {
        string resolved = await _resolver.ResolveAsync(
            "wins {count.wins}, losses {count.losses}",
            TwitchSeeds(),
            Channel
        );

        resolved.Should().Be("wins 7, losses 0");
    }

    [Fact]
    public async Task ViewerStats_ResolveFromTheM1Profile()
    {
        _analytics
            .GetProfileAsync(Channel, Alice, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        Alice,
                        "111",
                        "Alice",
                        new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc),
                        null,
                        TotalWatchSeconds: 7260,
                        TotalMessages: 42,
                        TotalCommandsUsed: 5,
                        TotalRedemptions: 2,
                        TotalSongRequests: 1,
                        IsFollower: true,
                        IsSubscriber: false,
                        SubTier: null,
                        IsAnalyticsOptedOut: false
                    )
                )
            );

        string resolved = await _resolver.ResolveAsync(
            "{viewer.messages} msgs, {viewer.watchtime}, since {viewer.firstseen}, "
                + "{viewer.redemptions} redeems, {viewer.songrequests} songs",
            TwitchSeeds(),
            Channel
        );

        resolved.Should().Be("42 msgs, 2h 1m, since 2026-01-05, 2 redeems, 1 songs");
    }

    [Fact]
    public async Task ViewerStats_ForANeverSeenViewer_RenderHonestZeros()
    {
        string resolved = await _resolver.ResolveAsync(
            "{viewer.messages} msgs, {viewer.watchtime}, since {viewer.firstseen}",
            TwitchSeeds(),
            Channel
        );

        resolved.Should().Be("0 msgs, 0m, since unknown");
    }

    [Fact]
    public async Task CallerSeeds_AlwaysWinOverAutoResolution()
    {
        Dictionary<string, string> seeds = TwitchSeeds();
        seeds["viewer.data.deaths"] = "OVERRIDDEN";
        seeds["count.wins"] = "OVERRIDDEN";

        string resolved = await _resolver.ResolveAsync(
            "{viewer.data.deaths} {count.wins}",
            seeds,
            Channel
        );

        resolved.Should().Be("OVERRIDDEN OVERRIDDEN");
    }
}
