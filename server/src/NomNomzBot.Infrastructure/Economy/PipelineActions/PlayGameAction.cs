// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Infrastructure.Economy.PipelineActions;

/// <summary>
/// Pipeline action <c>play_game</c> (economy.md §6): plays a game for the triggering viewer via
/// <see cref="IGameService"/> (the optional 18+ gate, bet bounds, and cooldown are enforced in the service).
/// Params: <c>game_type</c> (resolved to its config), <c>bet</c> (positive int). The role level is resolved
/// server-side. Writes <c>outcome</c>/<c>payout</c>/<c>net</c>/<c>balance</c> into the pipeline variables.
/// </summary>
public sealed class PlayGameAction(IGameService games, IRoleResolver roles) : ICommandAction
{
    public string ActionType => "play_game";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewer))
            return ActionResult.Failure("play_game requires a valid triggering viewer.");
        string gameType = action.GetString("game_type") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(gameType))
            return ActionResult.Failure("play_game requires a 'game_type'.");
        int bet = action.GetInt("bet");
        if (bet <= 0)
            return ActionResult.Failure("play_game requires a positive 'bet'.");

        Result<IReadOnlyList<GameConfigDto>> list = await games.ListGamesAsync(
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (list.IsFailure)
            return ActionResult.Failure(list.ErrorMessage ?? "play_game failed.");
        GameConfigDto? game = list.Value.FirstOrDefault(g =>
            string.Equals(g.GameType, gameType, StringComparison.OrdinalIgnoreCase)
        );
        if (game is null)
            return ActionResult.Failure($"Unknown game '{gameType}'.");

        Result<int> level = await roles.ResolveEffectiveLevelAsync(
            viewer,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        Result<GamePlayResultDto> play = await games.PlayAsync(
            ctx.BroadcasterId,
            new PlayGameRequest(game.Id, viewer, bet, level.IsSuccess ? level.Value : 0),
            ctx.CancellationToken
        );
        if (play.IsFailure)
            return ActionResult.Failure(play.ErrorMessage ?? "play_game failed.");

        ctx.Variables["outcome"] = play.Value.Outcome;
        ctx.Variables["payout"] = play.Value.PayoutAmount.ToString();
        ctx.Variables["net"] = play.Value.NetResult.ToString();
        ctx.Variables["balance"] = play.Value.BalanceAfter.ToString();
        return ActionResult.Success(play.Value.Outcome);
    }
}
