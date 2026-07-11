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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// The chat entry to the economy mini-games: <c>!coinflip|!dice|!slots &lt;bet&gt;</c> →
/// <see cref="IGameService.PlayAsync"/>. The chatter IS a (possibly not-set-up) User — resolved through the
/// same <see cref="IUserService.GetOrCreateAsync"/> seam every chat-ingest path uses. Every game rule
/// (enabled toggle, optional 18+ gate, bet range, cooldown, per-stream cap, standing floor) is enforced by
/// the service; this class only parses the bet and phrases the outcome for chat.
/// </summary>
public abstract class GamePlayBuiltinBase : IBuiltinCommand
{
    private readonly IGameService _games;
    private readonly IUserService _users;

    protected GamePlayBuiltinBase(IGameService games, IUserService users)
    {
        _games = games;
        _users = users;
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
        string mention = $"@{context.TriggeringUserDisplayName}";

        string betArg = context.Args.Trim().Split(' ', 2)[0];
        if (!long.TryParse(betArg, out long bet) || bet <= 0)
            return Result.Success($"{mention} Usage: !{GameType} <bet>");

        Result<UserDto> user = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid playerUserId))
            return Result.Success($"{mention} Could not resolve your account — try again.");

        // ListGamesAsync lazily seeds the default catalog, so a fresh channel resolves its config here too.
        Result<IReadOnlyList<GameConfigDto>> games = await _games.ListGamesAsync(
            context.BroadcasterId,
            ct
        );
        if (games.IsFailure)
            return Result.Success($"{mention} Games are unavailable right now.");

        GameConfigDto? game = games.Value.FirstOrDefault(g =>
            string.Equals(g.GameType, GameType, StringComparison.OrdinalIgnoreCase)
        );
        if (game is null || !game.IsEnabled)
            return Result.Success($"{mention} {GameType} is not enabled on this channel.");

        Result<GamePlayResultDto> played = await _games.PlayAsync(
            context.BroadcasterId,
            new PlayGameRequest(game.Id, playerUserId, bet, context.RoleLevel),
            ct
        );

        // The service's failure messages are already chat-friendly ("Bet is outside the allowed range.", …).
        if (played.IsFailure)
            return Result.Success($"{mention} {played.ErrorMessage}");

        GamePlayResultDto outcome = played.Value;
        return Result.Success(
            outcome.PayoutAmount > 0
                ? $"{mention} WON {outcome.PayoutAmount} on {GameType} (bet {outcome.BetAmount})! Balance: {outcome.BalanceAfter}"
                : $"{mention} lost {outcome.BetAmount} on {GameType}. Balance: {outcome.BalanceAfter}"
        );
    }
}

/// <summary>!coinflip &lt;bet&gt; — 50/50 fun-money flip through the game engine.</summary>
public sealed class CoinflipBuiltin(IGameService games, IUserService users)
    : GamePlayBuiltinBase(games, users)
{
    protected override string GameType => "coinflip";
}

/// <summary>!dice &lt;bet&gt; — dice roll through the game engine.</summary>
public sealed class DiceBuiltin(IGameService games, IUserService users)
    : GamePlayBuiltinBase(games, users)
{
    protected override string GameType => "dice";
}

/// <summary>!slots &lt;bet&gt; — slot pull through the game engine.</summary>
public sealed class SlotsBuiltin(IGameService games, IUserService users)
    : GamePlayBuiltinBase(games, users)
{
    protected override string GameType => "slots";
}
