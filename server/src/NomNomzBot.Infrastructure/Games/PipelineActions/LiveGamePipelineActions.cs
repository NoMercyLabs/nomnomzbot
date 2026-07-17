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
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;

namespace NomNomzBot.Infrastructure.Games.PipelineActions;

/// <summary>
/// Pipeline action <c>start_live_game</c> (live-games.md §6): opens a round of <c>game_type</c> via the
/// engine — so a <c>!dropgame</c> command, a redemption, or a timer can launch it. Fails closed when the
/// game is unknown/disabled or a session is already active (D7). Writes <c>session_id</c>/<c>status</c>
/// into the pipeline variables.
/// </summary>
public sealed class StartLiveGameAction(ILiveGameEngine engine) : ICommandAction
{
    public string ActionType => "start_live_game";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string gameType = action.GetString("game_type") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(gameType))
            return ActionResult.Failure("start_live_game requires a 'game_type'.");

        Guid? startedBy = Guid.TryParse(ctx.TriggeredByUserId, out Guid trigger) ? trigger : null;
        Result<GameSessionDto> started = await engine.StartAsync(
            ctx.BroadcasterId,
            new StartLiveGameCommand(gameType, startedBy),
            ctx.CancellationToken
        );
        if (started.IsFailure)
            return ActionResult.Failure(started.ErrorMessage ?? "start_live_game failed.");

        ctx.Variables["session_id"] = started.Value.Id.ToString();
        ctx.Variables["status"] = started.Value.Status;
        return ActionResult.Success($"start_live_game:{gameType} session={started.Value.Id}");
    }
}

/// <summary>
/// Pipeline action <c>cancel_live_game</c> (live-games.md §6): cancels the channel's active session
/// (refund + cancel). No-op success when none is active.
/// </summary>
public sealed class CancelLiveGameAction(ILiveGameEngine engine) : ICommandAction
{
    public string ActionType => "cancel_live_game";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<GameSessionDto> active = await engine.GetActiveAsync(
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (active.IsFailure)
            return ActionResult.Success("cancel_live_game: no active session.");

        Result cancelled = await engine.CancelAsync(
            ctx.BroadcasterId,
            active.Value.Id,
            ctx.CancellationToken
        );
        return cancelled.IsSuccess
            ? ActionResult.Success($"cancel_live_game: cancelled {active.Value.Id}")
            : ActionResult.Failure(cancelled.ErrorMessage ?? "cancel_live_game failed.");
    }
}
