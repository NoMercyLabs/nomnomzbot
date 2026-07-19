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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the <c>{{user.followAge}}</c> / <c>{{target.followAge}}</c> template helpers resolve a REAL follow
/// duration off the live Twitch follower list (Get Channel Followers, moderator:read:followers) instead of the
/// old hardcoded "unknown" placeholder — the parity gap that made a ported <c>!followage</c> command useless.
/// Every failure mode stays truthful: "not following" when there is no follow record, "unknown" only when the
/// Helix lookup itself fails; never a fabricated duration.
/// </summary>
public sealed class FollowAgeTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000e001");

    private readonly PronounGrammarTestDbContext _db;
    private readonly ITwitchChannelsApi _channels = Substitute.For<ITwitchChannelsApi>();
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    );
    private readonly TemplateResolver _resolver;

    public FollowAgeTemplateResolverTests()
    {
        _db = PronounGrammarTestDbContext.New();
        _db.Users.Add(
            new User
            {
                TwitchUserId = "555",
                Username = "eve",
                UsernameNormalized = "eve",
                DisplayName = "Eve",
            }
        );
        _db.SaveChanges();

        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Channels.Returns(_channels);

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton(helix);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            _time
        );
    }

    private async Task<string> ResolveFollowAge() =>
        await _resolver.ResolveAsync(
            "{user.followAge}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user.id"] = "555",
            },
            Channel
        );

    [Fact]
    public async Task Resolves_the_real_follow_duration_when_the_viewer_follows()
    {
        // Followed ~14 months before the fixed "now" → a concrete, non-placeholder age.
        _channels
            .GetChannelFollowerAsync(Channel, "555", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new TwitchChannelFollower(
                        "555",
                        "eve",
                        "Eve",
                        new DateTimeOffset(2024, 11, 1, 0, 0, 0, TimeSpan.Zero)
                    )
                )
            );

        string resolved = await ResolveFollowAge();

        resolved.Should().Contain("year").And.NotContain("unknown");
    }

    [Fact]
    public async Task Says_not_following_when_there_is_no_follow_record()
    {
        _channels
            .GetChannelFollowerAsync(Channel, "555", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TwitchChannelFollower?>(null));

        string resolved = await ResolveFollowAge();

        resolved.Should().Be("not following");
    }

    [Fact]
    public async Task Falls_back_to_unknown_only_when_the_lookup_itself_fails()
    {
        _channels
            .GetChannelFollowerAsync(Channel, "555", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchChannelFollower?>("Twitch unavailable.", "TWITCH_ERROR"));

        string resolved = await ResolveFollowAge();

        resolved.Should().Be("unknown");
    }
}
