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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves <c>{user.messageCount}</c> reads the same viewer-analytics aggregate as <c>{viewer.messages}</c>
/// (<see cref="IViewerAnalyticsService.GetProfileAsync"/>) instead of the old hardcoded "0" placeholder —
/// the parity gap tracked in BUILD-TODO.md and <c>usability-shortcomings-audit-scope-and-plan.md</c> §C7.
/// Never seen renders an honest zero; both keys resolve the exact same number for the same viewer.
/// </summary>
public sealed class UserMessageCountTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f001");
    private static readonly Guid Alice = Guid.Parse("0192b400-0000-7000-9000-00000000f002");

    private readonly PronounGrammarTestDbContext _db;
    private readonly IViewerAnalyticsService _analytics = Substitute.For<IViewerAnalyticsService>();
    private readonly TemplateResolver _resolver;

    public UserMessageCountTemplateResolverTests()
    {
        _db = PronounGrammarTestDbContext.New();
        _db.Users.Add(
            new()
            {
                Id = Alice,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        _db.SaveChanges();

        _analytics
            .GetProfileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ViewerProfileDto>("never seen", "NOT_FOUND"));

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton(_analytics);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Dictionary<string, string> AliceSeeds() =>
        new(StringComparer.OrdinalIgnoreCase) { ["user.id"] = "111", ["user.provider"] = "twitch" };

    [Fact]
    public async Task UserMessageCount_AliasesTheSameProfileAggregateAsViewerMessages()
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
                        TotalWatchSeconds: 0,
                        TotalMessages: 42,
                        TotalCommandsUsed: 0,
                        TotalRedemptions: 0,
                        TotalSongRequests: 0,
                        IsFollower: true,
                        IsSubscriber: false,
                        SubTier: null,
                        IsAnalyticsOptedOut: false
                    )
                )
            );

        string resolved = await _resolver.ResolveAsync(
            "{user.messageCount}",
            AliceSeeds(),
            Channel
        );

        // Same source as {viewer.messages} (TemplateResolverViewerDataTests.ViewerStats_ResolveFromTheM1Profile):
        // IViewerAnalyticsService.GetProfileAsync(...).TotalMessages — one real aggregate, not a second stub.
        resolved.Should().Be("42");
    }

    [Fact]
    public async Task UserMessageCount_ForANeverSeenViewer_RendersHonestZero()
    {
        string resolved = await _resolver.ResolveAsync(
            "{user.messageCount}",
            AliceSeeds(),
            Channel
        );

        resolved.Should().Be("0");
    }
}
