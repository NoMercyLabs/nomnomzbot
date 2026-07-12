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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.ViewerData.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.ViewerData;

/// <summary>
/// Proves <c>!stats</c>/<c>!profile</c> (per-viewer-data.md D4) COMPOSES the existing read-models —
/// analytics M.1 profile, M.3 streak, the economy wallet with a real rank over the channel's wallets —
/// for the caller or a known @target, degrades honestly for never-seen viewers, and feeds the full stat
/// variable set to a custom response template.
/// </summary>
public sealed class StatsBuiltinTests
{
    private static readonly Guid Channel = Guid.Parse("0192b300-0000-7000-8000-00000000c001");
    private static readonly Guid Alice = Guid.Parse("0192b300-0000-7000-8000-00000000a001");
    private static readonly Guid Bob = Guid.Parse("0192b300-0000-7000-8000-00000000a002");

    private readonly ViewerDataTestDbContext _db;
    private readonly IViewerAnalyticsService _analytics;
    private readonly ICurrencyAccountService _wallets;
    private readonly IUserService _users;
    private readonly ITemplateResolver _templates;

    public StatsBuiltinTests()
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
        _db.SaveChanges();

        _analytics = Substitute.For<IViewerAnalyticsService>();
        _analytics
            .GetProfileAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ViewerProfileDto>("never seen", "NOT_FOUND"));
        _analytics
            .GetStreakAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WatchStreakDto>("no streak", "NOT_FOUND"));

        _wallets = Substitute.For<ICurrencyAccountService>();
        _wallets
            .GetBalanceAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<long>("no wallet", "NOT_FOUND"));

        _users = Substitute.For<IUserService>();
        _users
            .GetOrCreateAsync(
                "111",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(Alice.ToString(), "alice", "Alice", null, null, default, default)
                )
            );

        _templates = Substitute.For<ITemplateResolver>();
        _templates
            .Resolve(Arg.Any<string>(), Arg.Any<IDictionary<string, string>>())
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return template;
            });
    }

    private StatsBuiltin Sut() => new(_analytics, _wallets, _users, _db, _templates);

    private static BuiltinCommandContext Context(string args = "") =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "111",
            TriggeringUserDisplayName = "Alice",
            TriggeringUserLogin = "alice",
            Args = args,
        };

    private void SeedAliceStats()
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
                        new DateTime(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc),
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
        _analytics
            .GetStreakAsync(Channel, Alice, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WatchStreakDto(3, 6, new DateOnly(2026, 7, 11))));
        _wallets
            .GetBalanceAsync(Channel, Alice, Arg.Any<CancellationToken>())
            .Returns(Result.Success(500L));
        // Two richer wallets in this channel → Alice ranks #3. A richer wallet in ANOTHER channel must not count.
        _db.CurrencyAccounts.AddRange(
            new CurrencyAccount
            {
                BroadcasterId = Channel,
                ViewerUserId = Bob,
                ViewerTwitchUserId = "hash-bob",
                Balance = 900,
            },
            new CurrencyAccount
            {
                BroadcasterId = Channel,
                ViewerUserId = Guid.NewGuid(),
                ViewerTwitchUserId = "hash-x",
                Balance = 700,
            },
            new CurrencyAccount
            {
                BroadcasterId = Guid.NewGuid(),
                ViewerUserId = Guid.NewGuid(),
                ViewerTwitchUserId = "hash-y",
                Balance = 9999,
            }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task Stats_ComposesMessagesWatchtimePointsRankStreakAndFirstSeen_ForTheCaller()
    {
        SeedAliceStats();

        Result<string> reply = await Sut().ExecuteAsync(Context());

        reply.IsSuccess.Should().BeTrue();
        reply.Value.Should().Contain("Alice");
        reply.Value.Should().Contain("42 messages");
        reply.Value.Should().Contain("2h 1m watched");
        reply.Value.Should().Contain("500 points (rank #3)");
        reply.Value.Should().Contain("3-stream streak");
        reply.Value.Should().Contain("first seen 2026-01-05");
    }

    [Fact]
    public async Task Stats_WithATarget_ComposesTheTargetsStats_NotTheCallers()
    {
        _analytics
            .GetProfileAsync(Channel, Bob, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        Bob,
                        "222",
                        "Bob",
                        new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        null,
                        TotalWatchSeconds: 60,
                        TotalMessages: 7,
                        TotalCommandsUsed: 0,
                        TotalRedemptions: 0,
                        TotalSongRequests: 0,
                        IsFollower: false,
                        IsSubscriber: false,
                        SubTier: null,
                        IsAnalyticsOptedOut: false
                    )
                )
            );

        Result<string> reply = await Sut().ExecuteAsync(Context("@Bob"));

        reply.Value.Should().Contain("Bob");
        reply.Value.Should().Contain("7 messages");
        reply.Value.Should().Contain("1m watched");
        // No wallet and no streak → those parts degrade honestly instead of inventing numbers.
        reply.Value.Should().Contain("0 points");
        reply.Value.Should().NotContain("rank #");
        reply.Value.Should().NotContain("streak");
    }

    [Fact]
    public async Task Stats_ForAnUnknownTarget_AnswersHonestly()
    {
        Result<string> reply = await Sut().ExecuteAsync(Context("@ghost"));

        reply.Value.Should().Be("I haven't seen ghost here yet.");
    }

    [Fact]
    public async Task Stats_ForANeverSeenCaller_AnswersHonestly()
    {
        Result<string> reply = await Sut().ExecuteAsync(Context());

        reply.Value.Should().Be("I haven't seen Alice chat here yet.");
    }

    [Fact]
    public async Task Stats_CustomTemplate_ReceivesTheFullStatVariableSet()
    {
        SeedAliceStats();
        BuiltinCommandContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "111",
            TriggeringUserDisplayName = "Alice",
            TriggeringUserLogin = "alice",
            Args = string.Empty,
            CustomResponseTemplate =
                "{stats.user}: {stats.points} pts (#{stats.rank}), {stats.messages} msgs, "
                + "{stats.watchtime}, streak {stats.streak}, since {stats.firstseen}",
        };

        Result<string> reply = await Sut().ExecuteAsync(ctx);

        reply.Value.Should().Be("Alice: 500 pts (#3), 42 msgs, 2h 1m, streak 3, since 2026-01-05");
    }

    [Fact]
    public async Task Stats_WithAFlavoredTone_RendersAToneTemplate_NotTheNeutralLine()
    {
        SeedAliceStats();
        BuiltinCommandContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "111",
            TriggeringUserDisplayName = "Alice",
            TriggeringUserLogin = "alice",
            Args = string.Empty,
            Personality = PersonalityTone.Sassy,
        };

        Result<string> reply = await Sut().ExecuteAsync(ctx);

        HashSet<string> expected = ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Stats.Key,
                BuiltinResponseSlots.Stats.Profile
            )
            .Select(t =>
                t.Replace("{stats.user}", "Alice")
                    .Replace("{stats.messages}", "42")
                    .Replace("{stats.watchtime}", "2h 1m")
                    .Replace("{stats.points}", "500")
                    .Replace("{stats.rank}", "3")
                    .Replace("{stats.streak}", "3")
                    .Replace("{stats.firstseen}", "2026-01-05")
            )
            .ToHashSet();

        expected.Should().Contain(reply.Value);
        // The neutral composed line uses " · " separators — a tone template must not be that line.
        reply.Value.Should().NotContain(" · ");
    }

    [Fact]
    public async Task Stats_WithTheDefaultInformativeTone_KeepsTheRichNeutralLine()
    {
        // Informative is intentionally NOT authored for !stats, so the default keeps the richer conditional
        // line (rank + streak) — proving the AddFlavored omission behaves as designed.
        SeedAliceStats();
        BuiltinCommandContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "111",
            TriggeringUserDisplayName = "Alice",
            TriggeringUserLogin = "alice",
            Args = string.Empty,
            Personality = PersonalityTone.Informative,
        };

        Result<string> reply = await Sut().ExecuteAsync(ctx);

        reply.Value.Should().Contain("500 points (rank #3)");
        reply.Value.Should().Contain("3-stream streak");
    }

    [Fact]
    public async Task Profile_IsTheLegacyParityAliasOfStats()
    {
        SeedAliceStats();
        ProfileBuiltin alias = new(_analytics, _wallets, _users, _db, _templates);

        alias.BuiltinKey.Should().Be("profile");
        Result<string> reply = await alias.ExecuteAsync(Context());
        reply.Value.Should().Contain("42 messages");
    }
}
