// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Economy.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>The economy pickers: the channel's games and the savings jars it can contribute to.</summary>
public sealed class EconomyPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();

    private static GameConfigDto Game(string type, string category, bool enabled) =>
        new(
            Guid.NewGuid(),
            type,
            category,
            enabled,
            false,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            null,
            "everyone",
            null
        );

    private static SavingsJarDto Jar(Guid id, string name, long balance, long? goal) =>
        new(
            id,
            Broadcaster,
            name,
            null,
            goal,
            balance,
            null,
            true,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

    [Fact]
    public void Each_provider_serves_its_own_kind()
    {
        Assert.Equal(
            PipelineActionFieldKind.GameType,
            new GameTypeOptionProvider(Substitute.For<IGameService>()).Kind
        );
        Assert.Equal(
            PipelineActionFieldKind.SavingsJar,
            new SavingsJarOptionProvider(Substitute.For<ISavingsJarService>()).Kind
        );
    }

    // ── game type ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Game_type_lists_the_channels_games_with_the_type_saved_and_the_category_shown()
    {
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<GameConfigDto>>([
                    Game("slots", "gambling", true),
                    Game("duel", "social", false),
                ])
            );

        Result<PipelineOptionListResult> result = await new GameTypeOptionProvider(
            games
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["duel", "slots"], PickerAsk.Values(result));
        Assert.Equal("social, disabled", result.Value.Items[0].SecondaryText);
        Assert.Equal("gambling", result.Value.Items[1].SecondaryText);
        Assert.All(result.Value.Items, o => Assert.Equal(PipelineOptionState.Selectable, o.State));
    }

    [Fact]
    public async Task Game_type_search_value_lookup_and_paging_narrow_the_list()
    {
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<GameConfigDto>>([
                    Game("slots", "gambling", true),
                    Game("duel", "social", true),
                ])
            );
        GameTypeOptionProvider provider = new(games);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "SLO")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "duel")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(["slots"], PickerAsk.Values(searched));
        Assert.Equal(["duel"], PickerAsk.Values(looked));
        Assert.Equal(["slots"], PickerAsk.Values(second));
        Assert.Equal(2, second.Value.TotalCount);
    }

    [Fact]
    public async Task Game_type_is_unavailable_with_the_reason_when_the_games_cannot_be_read()
    {
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<GameConfigDto>>("Games are offline.", "OFFLINE"));

        PickerAsk.AssertUnavailable(
            await new GameTypeOptionProvider(games).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            "Games are offline."
        );
    }

    [Fact]
    public async Task Game_type_asks_the_service_for_the_asking_channel_only()
    {
        Guid other = Guid.NewGuid();
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(other, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<GameConfigDto>>([Game("others-game", "x", true)])
            );
        games
            .ListGamesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<GameConfigDto>>([Game("mine", "x", true)]));

        Result<PipelineOptionListResult> result = await new GameTypeOptionProvider(
            games
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["mine"], PickerAsk.Values(result));
    }

    // ── savings jar ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Jar_lists_the_channels_jars_with_the_id_saved_and_balance_against_goal_shown()
    {
        Guid withGoal = Guid.NewGuid();
        Guid noGoal = Guid.NewGuid();
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ListJarsForChannelAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<SavingsJarDto>>([
                    Jar(withGoal, "Vacation", 1200, 5000),
                    Jar(noGoal, "Pizza", 30, null),
                ])
            );

        Result<PipelineOptionListResult> result = await new SavingsJarOptionProvider(
            jars
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal([noGoal.ToString(), withGoal.ToString()], PickerAsk.Values(result));
        Assert.Equal("Pizza", result.Value.Items[0].Label);
        Assert.Equal("30", result.Value.Items[0].SecondaryText);
        Assert.Equal("1200 of 5000", result.Value.Items[1].SecondaryText);
    }

    [Fact]
    public async Task Jar_value_lookup_finds_a_saved_ulid_and_a_missing_jar_is_an_empty_available_list()
    {
        Guid id = Guid.NewGuid();
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ListJarsForChannelAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SavingsJarDto>>([Jar(id, "Vacation", 1, null)]));
        SavingsJarOptionProvider provider = new(jars);

        Result<PipelineOptionListResult> byUlid = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: OwnedIdCodec.Encode(id))
        );
        Result<PipelineOptionListResult> gone = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: OwnedIdCodec.Encode(Guid.NewGuid()))
        );

        Assert.Equal([id.ToString()], PickerAsk.Values(byUlid));
        Assert.True(gone.Value.SourceAvailable);
        Assert.Empty(gone.Value.Items);
    }

    [Fact]
    public async Task Jar_search_and_paging_narrow_the_list_and_an_unreadable_source_is_unavailable()
    {
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ListJarsForChannelAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<SavingsJarDto>>([
                    Jar(Guid.NewGuid(), "Vacation", 1, null),
                    Jar(Guid.NewGuid(), "Pizza", 1, null),
                ])
            );
        SavingsJarOptionProvider provider = new(jars);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "piz")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );
        ISavingsJarService broken = Substitute.For<ISavingsJarService>();
        broken
            .ListJarsForChannelAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SavingsJarDto>>("Jars are offline.", "OFFLINE"));

        Assert.Equal(["Pizza"], searched.Value.Items.Select(i => i.Label));
        Assert.Equal(["Vacation"], second.Value.Items.Select(i => i.Label));
        Assert.Equal(2, second.Value.TotalCount);
        PickerAsk.AssertUnavailable(
            await new SavingsJarOptionProvider(broken).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            "Jars are offline."
        );
    }

    [Fact]
    public async Task Jar_list_is_read_for_the_asking_channel_so_another_channels_jar_never_appears()
    {
        Guid other = Guid.NewGuid();
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ListJarsForChannelAsync(other, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<SavingsJarDto>>([
                    Jar(Guid.NewGuid(), "Theirs", 1, null),
                ])
            );
        jars.ListJarsForChannelAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<SavingsJarDto>>([Jar(Guid.NewGuid(), "Mine", 1, null)])
            );

        Result<PipelineOptionListResult> result = await new SavingsJarOptionProvider(
            jars
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["Mine"], result.Value.Items.Select(i => i.Label));
    }
}
