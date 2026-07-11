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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// Proves the chat→game bridge: <c>!coinflip &lt;bet&gt;</c> resolves the chatter to a User, plays through
/// <see cref="IGameService.PlayAsync"/> with the parsed bet and the caller's badge level, and phrases the
/// real outcome (win / lose / rule rejection) back for chat — the command was previously dead air.
/// </summary>
public sealed class GameBuiltinsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000a101");
    private static readonly Guid GameId = Guid.Parse("0192a000-0000-7000-8000-00000000a102");
    private static readonly Guid PlayerId = Guid.Parse("0192a000-0000-7000-8000-00000000a103");

    private static BuiltinCommandContext Context(string args) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-1",
            TriggeringUserDisplayName = "Viewer",
            TriggeringUserLogin = "viewer",
            RoleLevel = 2,
            Args = args,
        };

    private static GameConfigDto Config(bool enabled) =>
        new(
            Id: GameId,
            GameType: "coinflip",
            Category: "Gambling",
            IsEnabled: enabled,
            Requires18Plus: false,
            MinBet: null,
            MaxBet: null,
            HouseEdgePercent: 5m,
            WinChancePercent: 50m,
            PayoutMultiplier: 1.9m,
            CooldownSeconds: 0,
            MaxPlaysPerStream: null,
            Permission: "Everyone",
            Config: null
        );

    private static (CoinflipBuiltin Sut, IGameService Games) Build(
        bool enabled = true,
        Result<GamePlayResultDto>? playResult = null
    )
    {
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<GameConfigDto>>([Config(enabled)]));
        if (playResult is not null)
            games
                .PlayAsync(Channel, Arg.Any<PlayGameRequest>(), Arg.Any<CancellationToken>())
                .Returns(playResult);

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-1",
                "viewer",
                "Viewer",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Id: PlayerId.ToString(),
                        Username: "viewer",
                        DisplayName: "Viewer",
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UnixEpoch,
                        LastLoginAt: DateTime.UnixEpoch
                    )
                )
            );

        return (new CoinflipBuiltin(games, users), games);
    }

    [Fact]
    public async Task A_missing_or_invalid_bet_replies_usage_without_playing()
    {
        (CoinflipBuiltin sut, IGameService games) = Build();

        Result<string> none = await sut.ExecuteAsync(Context(""));
        Result<string> junk = await sut.ExecuteAsync(Context("all-in"));

        none.Value.Should().Contain("Usage: !coinflip <bet>");
        junk.Value.Should().Contain("Usage: !coinflip <bet>");
        await games.DidNotReceiveWithAnyArgs().PlayAsync(default, default!, default);
    }

    [Fact]
    public async Task A_disabled_game_replies_not_enabled_without_playing()
    {
        (CoinflipBuiltin sut, IGameService games) = Build(enabled: false);

        Result<string> reply = await sut.ExecuteAsync(Context("50"));

        reply.Value.Should().Contain("not enabled");
        await games.DidNotReceiveWithAnyArgs().PlayAsync(default, default!, default);
    }

    [Fact]
    public async Task A_win_plays_with_the_parsed_bet_and_caller_identity_and_phrases_the_payout()
    {
        (CoinflipBuiltin sut, IGameService games) = Build(
            playResult: Result.Success(
                new GamePlayResultDto(
                    Id: 7,
                    GameType: "coinflip",
                    Outcome: "Win",
                    BetAmount: 50,
                    PayoutAmount: 95,
                    NetResult: 45,
                    BalanceAfter: 1045,
                    Result: null
                )
            )
        );

        Result<string> reply = await sut.ExecuteAsync(Context("50"));

        reply.Value.Should().Contain("WON 95").And.Contain("Balance: 1045");
        await games
            .Received(1)
            .PlayAsync(
                Channel,
                Arg.Is<PlayGameRequest>(r =>
                    r.GameConfigId == GameId
                    && r.PlayerUserId == PlayerId
                    && r.BetAmount == 50
                    && r.RoleLevel == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_loss_phrases_the_bet_and_new_balance()
    {
        (CoinflipBuiltin sut, _) = Build(
            playResult: Result.Success(
                new GamePlayResultDto(
                    Id: 8,
                    GameType: "coinflip",
                    Outcome: "Lose",
                    BetAmount: 50,
                    PayoutAmount: 0,
                    NetResult: -50,
                    BalanceAfter: 950,
                    Result: null
                )
            )
        );

        Result<string> reply = await sut.ExecuteAsync(Context("50"));

        reply.Value.Should().Contain("lost 50").And.Contain("Balance: 950");
    }

    [Fact]
    public async Task A_rule_rejection_relays_the_services_chat_friendly_message()
    {
        (CoinflipBuiltin sut, _) = Build(
            playResult: Result.Failure<GamePlayResultDto>(
                "Bet is outside the allowed range.",
                "BET_OUT_OF_RANGE"
            )
        );

        Result<string> reply = await sut.ExecuteAsync(Context("999999"));

        reply.Value.Should().Contain("Bet is outside the allowed range.");
    }
}
