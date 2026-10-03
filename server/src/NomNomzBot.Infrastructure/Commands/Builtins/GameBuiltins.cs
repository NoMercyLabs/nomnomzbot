// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// The chat entry to the economy mini-games: <c>!coinflip|!dice|!slots &lt;bet&gt;</c> →
/// <see cref="IGameService.PlayAsync"/>. The chatter IS a (possibly not-set-up) User — resolved through the
/// same <see cref="IUserService.GetOrCreateAsync"/> seam every chat-ingest path uses. Every game rule
/// (enabled toggle, optional 18+ gate, bet range, cooldown, per-stream cap, standing floor) is enforced by
/// the service; this class only parses the bet and maps every outcome (played, refused, failed) to a reply
/// slot under the game's own trigger key, so the channel can re-word each one.
/// </summary>
public abstract class GamePlayBuiltinBase : IBuiltinCommand
{
    private readonly IGameService _games;
    private readonly IUserService _users;
    private readonly IBuiltinResponseComposer _composer;

    /// <summary>Which reply slot each service refusal code speaks with; any other code is <c>playfailed</c>.</summary>
    private static readonly IReadOnlyDictionary<string, string> FailureSlots = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["NOT_FOUND"] = BuiltinResponseSlots.Game.NotEnabled,
        ["GAMBLING_DISABLED"] = BuiltinResponseSlots.Game.NotEnabled,
        ["AGE_CONSENT_REQUIRED"] = BuiltinResponseSlots.Game.AgeConsentRequired,
        ["FORBIDDEN"] = BuiltinResponseSlots.Game.NotAllowed,
        ["BET_OUT_OF_RANGE"] = BuiltinResponseSlots.Game.BetOutOfRange,
        ["ON_COOLDOWN"] = BuiltinResponseSlots.Game.OnCooldown,
        ["PER_STREAM_LIMIT"] = BuiltinResponseSlots.Game.StreamLimit,
        ["INSUFFICIENT_FUNDS"] = BuiltinResponseSlots.Game.InsufficientFunds,
        ["CURRENCY_DISABLED"] = BuiltinResponseSlots.Game.CurrencyDisabled,
    };

    protected GamePlayBuiltinBase(
        IGameService games,
        IUserService users,
        IBuiltinResponseComposer composer
    )
    {
        _games = games;
        _users = users;
        _composer = composer;
    }

    /// <summary>The <c>GameConfig.GameType</c> this command plays — also the chat trigger word.</summary>
    protected abstract string GameType { get; }

    public string BuiltinKey => GameType;

    // The game's own CooldownSeconds config governs pacing — no second cooldown layer here.
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        // Resolve the game config FIRST (ListGamesAsync lazily seeds the default catalog, so a fresh channel
        // resolves here too). Checking enablement before the bet-usage hint gives a disabled game ONE consistent,
        // actionable answer — instead of a "Usage:" line for a bare `!game` (which implies it works) followed by
        // "not enabled" once a bet is added. Games are opt-in (seeded disabled) and spend channel currency, so the
        // message points at both switches: enable the game AND set up the economy.
        Result<IReadOnlyList<GameConfigDto>> games = await _games.ListGamesAsync(
            context.BroadcasterId,
            ct
        );
        if (games.IsFailure)
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Game.Unavailable,
                    "Games are unavailable right now.",
                    ct
                )
            );

        GameConfigDto? game = games.Value.FirstOrDefault(g =>
            string.Equals(g.GameType, GameType, StringComparison.OrdinalIgnoreCase)
        );
        if (game is null || !game.IsEnabled)
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Game.NotEnabled,
                    $"!{GameType} isn't enabled yet — turn it on under Economy → Games. Games spend your channel currency, so set that up there too.",
                    ct
                )
            );

        // The response is sent as a reply to the caller's message, so no "@user" prefix is needed.
        string betArg = context.Args.Trim().Split(' ', 2)[0];
        if (!long.TryParse(betArg, out long bet) || bet <= 0)
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Game.Usage,
                    $"Usage: !{GameType} <bet>",
                    ct
                )
            );

        Result<UserDto> user = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            provider: context.TriggeringPlatform ?? AuthEnums.Platform.Twitch,
            cancellationToken: ct
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid playerUserId))
        {
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Game.AccountUnresolved,
                    "Could not resolve your account — try again.",
                    ct
                )
            );
        }

        Result<GamePlayResultDto> played = await _games.PlayAsync(
            context.BroadcasterId,
            new(game.Id, playerUserId, bet, context.RoleLevel),
            ct
        );

        // The service keeps its own ErrorMessage for logs and the API; chat speaks from the code's slot.
        if (played.IsFailure)
            return Result.Success(
                await ReplyAsync(
                    context,
                    FailureSlots.GetValueOrDefault(
                        played.ErrorCode ?? string.Empty,
                        BuiltinResponseSlots.Game.PlayFailed
                    ),
                    played.ErrorMessage ?? "That didn't work — try again.",
                    ct
                )
            );

        GamePlayResultDto outcome = played.Value;
        bool won = outcome.PayoutAmount > 0;
        return Result.Success(
            await ReplyAsync(
                context,
                won ? BuiltinResponseSlots.Game.Won : BuiltinResponseSlots.Game.Lost,
                won
                    ? $"You won {outcome.PayoutAmount} on {GameType} (bet {outcome.BetAmount})! Balance: {outcome.BalanceAfter}"
                    : $"You lost {outcome.BetAmount} on {GameType}. Balance: {outcome.BalanceAfter}",
                ct,
                new Dictionary<string, string>
                {
                    ["game.bet"] = outcome.BetAmount.ToString(),
                    ["game.payout"] = outcome.PayoutAmount.ToString(),
                    ["game.balance"] = outcome.BalanceAfter.ToString(),
                }
            )
        );
    }

    /// <summary>Composes one of this game's reply slots; <c>{game.name}</c> is always the game's trigger word.</summary>
    private Task<string> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? extra = null
    )
    {
        Dictionary<string, string> variables = new() { ["game.name"] = GameType };
        if (extra is not null)
            foreach (KeyValuePair<string, string> pair in extra)
                variables[pair.Key] = pair.Value;

        return _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ct
        );
    }
}

/// <summary>!coinflip &lt;bet&gt; — 50/50 fun-money flip through the game engine.</summary>
public sealed class CoinflipBuiltin(
    IGameService games,
    IUserService users,
    IBuiltinResponseComposer composer
) : GamePlayBuiltinBase(games, users, composer)
{
    protected override string GameType => "coinflip";
}

/// <summary>!dice &lt;bet&gt; — dice roll through the game engine.</summary>
public sealed class DiceBuiltin(
    IGameService games,
    IUserService users,
    IBuiltinResponseComposer composer
) : GamePlayBuiltinBase(games, users, composer)
{
    protected override string GameType => "dice";
}

/// <summary>!slots &lt;bet&gt; — slot pull through the game engine.</summary>
public sealed class SlotsBuiltin(
    IGameService games,
    IUserService users,
    IBuiltinResponseComposer composer
) : GamePlayBuiltinBase(games, users, composer)
{
    protected override string GameType => "slots";
}
