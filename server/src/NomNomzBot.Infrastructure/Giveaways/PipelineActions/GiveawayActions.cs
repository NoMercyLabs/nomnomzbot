// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways.PipelineActions;

/// <summary>
/// Pipeline action <c>open_giveaway</c> (giveaways.md §5): opens a prepared giveaway — a command or
/// timer can kick a campaign off. Params: <c>giveaway_id</c>. Fails closed when another giveaway is
/// already active (D2).
/// </summary>
public sealed class OpenGiveawayAction(IGiveawayService giveaways) : ICommandAction
{
    public string ActionType => "open_giveaway";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(action.GetString("giveaway_id"), out Guid giveawayId))
            return ActionResult.Failure("open_giveaway requires a 'giveaway_id'.");

        Result<GiveawayDto> opened = await giveaways.OpenAsync(
            ctx.BroadcasterId,
            giveawayId,
            ctx.CancellationToken
        );
        return opened.IsSuccess
            ? ActionResult.Success(opened.Value.Title)
            : ActionResult.Failure(opened.ErrorMessage ?? "open_giveaway failed.");
    }
}

/// <summary>
/// Pipeline action <c>draw_giveaway</c> (giveaways.md §5): draws the given giveaway, or the channel's
/// ACTIVE one when <c>giveaway_id</c> is omitted.
/// </summary>
public sealed class DrawGiveawayAction(IGiveawayService giveaways, IApplicationDbContext db)
    : ICommandAction
{
    public string ActionType => "draw_giveaway";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Guid? giveawayId = Guid.TryParse(action.GetString("giveaway_id"), out Guid parsed)
            ? parsed
            : await GiveawayActionSupport.ResolveActiveAsync(
                db,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
        if (giveawayId is null)
            return ActionResult.Failure("No active giveaway to draw.");

        Result<IReadOnlyList<GiveawayWinnerDto>> drawn = await giveaways.DrawAsync(
            ctx.BroadcasterId,
            giveawayId.Value,
            ctx.CancellationToken
        );
        if (drawn.IsFailure)
            return ActionResult.Failure(drawn.ErrorMessage ?? "draw_giveaway failed.");

        string winners = string.Join(", ", drawn.Value.Select(w => w.ViewerDisplayName));
        ctx.Variables["giveaway.winners"] = winners;
        return ActionResult.Success(winners);
    }
}

/// <summary>
/// Pipeline action <c>enter_giveaway</c> (giveaways.md §5): enters the TRIGGERING viewer — so a
/// channel-point redemption can be "redeem to enter". Fails closed when no giveaway is open or the
/// viewer is ineligible.
/// </summary>
public sealed class EnterGiveawayAction(IGiveawayService giveaways, IApplicationDbContext db)
    : ICommandAction
{
    public string ActionType => "enter_giveaway";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewerUserId))
            return ActionResult.Failure("enter_giveaway requires a valid triggering viewer.");

        Guid? giveawayId = Guid.TryParse(action.GetString("giveaway_id"), out Guid parsed)
            ? parsed
            : await GiveawayActionSupport.ResolveActiveAsync(
                db,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
        if (giveawayId is null)
            return ActionResult.Failure("No active giveaway to enter.");

        Result<GiveawayEntryDto> entered = await giveaways.EnterAsync(
            ctx.BroadcasterId,
            giveawayId.Value,
            viewerUserId,
            ctx.CancellationToken
        );
        return entered.IsSuccess
            ? ActionResult.Success(entered.Value.TicketCount.ToString())
            : ActionResult.Failure(entered.ErrorMessage ?? "enter_giveaway failed.");
    }
}

/// <summary>Shared "the active giveaway" resolution for the null-id action forms.</summary>
internal static class GiveawayActionSupport
{
    public static async Task<Guid?> ResolveActiveAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Guid id = await db
            .Giveaways.AsNoTracking()
            .Where(g =>
                g.BroadcasterId == broadcasterId
                && (g.Status == GiveawayStatus.Open || g.Status == GiveawayStatus.Closed)
            )
            .Select(g => g.Id)
            .FirstOrDefaultAsync(ct);
        return id == Guid.Empty ? null : id;
    }
}
