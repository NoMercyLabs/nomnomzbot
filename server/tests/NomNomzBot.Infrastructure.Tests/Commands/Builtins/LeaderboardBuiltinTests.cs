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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!leaderboard</c> (legacy parity, S068d) proves the reply is built from the REAL ranked entries
/// returned by <see cref="IEconomyLeaderboardService.GetRankingAsync"/> — seeded fake entries must show up
/// verbatim in the rendered text, not a hardcoded string — and that a channel with no configured leaderboard
/// gets a truthful "none configured" reply instead of fabricated data.
/// </summary>
public sealed class LeaderboardBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000a501");
    private static readonly Guid ConfigId = Guid.Parse("0192a000-0000-7000-8000-00000000a502");

    private static ITemplateResolver FakeResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return resolver;
    }

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer",
        };

    private static LeaderboardConfigDto Config(bool isPublic) =>
        new(
            ConfigId,
            Broadcaster,
            JarId: null,
            Metric: "points",
            Scope: "channel",
            Period: "alltime",
            IsPublic: isPublic,
            TopN: 5,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

    [Fact]
    public async Task Reply_contains_the_real_queried_ranking_entries_not_a_hardcoded_string()
    {
        IEconomyLeaderboardService leaderboards = Substitute.For<IEconomyLeaderboardService>();
        leaderboards
            .ListConfigsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<LeaderboardConfigDto>>([Config(isPublic: true)]));

        List<LeaderboardEntryDto> entries =
        [
            new(1, Guid.NewGuid(), Guid.NewGuid(), "TopFan", 900),
            new(2, Guid.NewGuid(), Guid.NewGuid(), "SecondPlace", 650),
            new(3, Guid.NewGuid(), Guid.NewGuid(), "ThirdWheel", 400),
        ];
        leaderboards
            .GetRankingAsync(Broadcaster, ConfigId, 5, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<LeaderboardEntryDto>>(entries));

        LeaderboardBuiltin sut = new(leaderboards, new BuiltinResponseComposer(FakeResolver()));

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("TopFan").And.Contain("900");
        result.Value.Should().Contain("SecondPlace").And.Contain("650");
        result.Value.Should().Contain("ThirdWheel").And.Contain("400");
        result.Value.Should().Contain("points");
    }

    [Fact]
    public async Task No_configured_leaderboard_reports_truthfully_instead_of_fabricating_rankings()
    {
        IEconomyLeaderboardService leaderboards = Substitute.For<IEconomyLeaderboardService>();
        leaderboards
            .ListConfigsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<LeaderboardConfigDto>>([]));

        LeaderboardBuiltin sut = new(leaderboards, new BuiltinResponseComposer(FakeResolver()));

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("No leaderboard is configured");

        await leaderboards
            .DidNotReceive()
            .GetRankingAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
