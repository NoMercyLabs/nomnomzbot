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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
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

    private static BuiltinCommandContext Context(
        string args,
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-1",
            TriggeringUserDisplayName = "Viewer",
            TriggeringUserLogin = "viewer",
            RoleLevel = 2,
            Args = args,
            Personality = personality,
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
        Result<GamePlayResultDto>? playResult = null,
        FakeChannelBuiltinReplies? channelReplies = null
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

        return (new(games, users, TestBuiltinComposer.Create(channelReplies)), games);
    }

    private static (CoinflipBuiltin Sut, IUserService Users) BuildWithUnresolvedAccount()
    {
        IGameService games = Substitute.For<IGameService>();
        games
            .ListGamesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<GameConfigDto>>([Config(enabled: true)]));

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-1",
                "viewer",
                "Viewer",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<UserDto>("account lookup failed", "ACCOUNT_ERROR"));

        return (new(games, users, TestBuiltinComposer.Create()), users);
    }

    [Fact]
    public async Task A_missing_or_invalid_bet_replies_usage_without_playing()
    {
        (CoinflipBuiltin sut, IGameService games) = Build();

        Result<string> none = await sut.ExecuteAsync(Context(""));
        Result<string> junk = await sut.ExecuteAsync(Context("all-in"));

        none.Value.Should().Be("Usage: !coinflip <bet>");
        junk.Value.Should().Be("Usage: !coinflip <bet>");
        await games.DidNotReceiveWithAnyArgs().PlayAsync(default, default!, default);
    }

    [Fact]
    public async Task A_disabled_game_replies_not_enabled_without_playing()
    {
        (CoinflipBuiltin sut, IGameService games) = Build(enabled: false);

        Result<string> reply = await sut.ExecuteAsync(Context("50"));

        // Actionable: names the game, that it isn't on, and where to turn it on (+ the currency dependency).
        reply.Value.Should().Contain("isn't enabled").And.Contain("Economy");
        await games.DidNotReceiveWithAnyArgs().PlayAsync(default, default!, default);
    }

    [Fact]
    public async Task A_disabled_game_replies_not_enabled_even_for_a_bare_command_not_usage()
    {
        // Enablement is checked BEFORE the bet-usage hint, so a disabled game gives ONE consistent answer whether
        // or not a bet is supplied — previously a bare `!coinflip` wrongly replied "Usage:" as if it worked.
        (CoinflipBuiltin sut, IGameService games) = Build(enabled: false);

        Result<string> reply = await sut.ExecuteAsync(Context(""));

        reply.Value.Should().Contain("isn't enabled");
        reply.Value.Should().NotContain("Usage");
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

        reply.Value.Should().Be("You won 95 on coinflip (bet 50)! Balance: 1045");
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

        reply.Value.Should().Be("You lost 50 on coinflip. Balance: 950");
    }

    [Fact]
    public async Task A_channel_override_of_the_won_slot_rewords_only_the_win()
    {
        FakeChannelBuiltinReplies replies = new FakeChannelBuiltinReplies().Set(
            Channel,
            "coinflip",
            BuiltinResponseSlots.Game.Won,
            "Nice flip! +{game.payout}, now at {game.balance}."
        );
        (CoinflipBuiltin winner, _) = Build(
            playResult: Result.Success(
                new GamePlayResultDto(9, "coinflip", "Win", 50, 95, 45, 1045, null)
            ),
            channelReplies: replies
        );
        (CoinflipBuiltin loser, _) = Build(
            playResult: Result.Success(
                new GamePlayResultDto(10, "coinflip", "Lose", 50, 0, -50, 950, null)
            ),
            channelReplies: replies
        );

        Result<string> won = await winner.ExecuteAsync(Context("50"));
        Result<string> lost = await loser.ExecuteAsync(Context("50"));

        won.Value.Should().Be("Nice flip! +95, now at 1045.");
        lost.Value.Should().Be("You lost 50 on coinflip. Balance: 950");
    }

    [Theory]
    [InlineData("INSUFFICIENT_FUNDS", "Insufficient funds.")]
    [InlineData("ON_COOLDOWN", "Game is on cooldown.")]
    [InlineData("PER_STREAM_LIMIT", "Per-stream play limit reached for this game.")]
    [InlineData("FORBIDDEN", "Insufficient role to play this game.")]
    [InlineData("AGE_CONSENT_REQUIRED", "This game requires confirming you are 18 or older.")]
    [InlineData("SOMETHING_NEW", "That didn't work — try again.")]
    public async Task Every_service_refusal_code_speaks_from_its_own_slot(
        string code,
        string expectedLine
    )
    {
        (CoinflipBuiltin sut, _) = Build(
            playResult: Result.Failure<GamePlayResultDto>("internal detail", code)
        );

        Result<string> reply = await sut.ExecuteAsync(Context("50"));

        reply.Value.Should().Be(expectedLine);
    }

    [Fact]
    public async Task A_channel_override_of_the_insufficient_funds_slot_replaces_that_refusal_only()
    {
        FakeChannelBuiltinReplies replies = new FakeChannelBuiltinReplies().Set(
            Channel,
            "coinflip",
            BuiltinResponseSlots.Game.InsufficientFunds,
            "Broke! Chat more."
        );
        (CoinflipBuiltin broke, _) = Build(
            playResult: Result.Failure<GamePlayResultDto>(
                "Insufficient funds.",
                "INSUFFICIENT_FUNDS"
            ),
            channelReplies: replies
        );
        (CoinflipBuiltin cooling, _) = Build(
            playResult: Result.Failure<GamePlayResultDto>("Game is on cooldown.", "ON_COOLDOWN"),
            channelReplies: replies
        );

        (await broke.ExecuteAsync(Context("50"))).Value.Should().Be("Broke! Chat more.");
        (await cooling.ExecuteAsync(Context("50"))).Value.Should().Be("Game is on cooldown.");
    }

    [Fact]
    public async Task A_rule_rejection_speaks_the_slot_line_not_the_service_message()
    {
        (CoinflipBuiltin sut, _) = Build(
            playResult: Result.Failure<GamePlayResultDto>(
                "Bet is outside the allowed range.",
                "BET_OUT_OF_RANGE"
            )
        );

        Result<string> reply = await sut.ExecuteAsync(Context("999999"));

        reply.Value.Should().Be("Bet is outside the allowed range.");
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_account_unresolved_message_than_the_default_tone()
    {
        (CoinflipBuiltin sut, _) = BuildWithUnresolvedAccount();

        Result<string> sassy = await sut.ExecuteAsync(Context("50", PersonalityTone.Sassy));
        Result<string> informative = await sut.ExecuteAsync(
            Context("50", PersonalityTone.Informative)
        );

        informative.Value.Should().Be("Could not resolve your account — try again.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(PersonalityTone.Sassy, "coinflip", BuiltinResponseSlots.Game.AccountUnresolved)
            .Should()
            .Contain(sassy.Value);
    }
}
