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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// The one validator and applier of a binding target that arrives in a request. Every surface that stores a
/// <see cref="BindingTarget"/> (reward, timer, event response, chat trigger, scheduled task, giveaway prize,
/// catalog item) calls these two methods and never re-implements them.
/// </summary>
public static class BindingTargetBinder
{
    /// <summary>
    /// A command target must name a command of this channel, a pipeline target a pipeline of this channel,
    /// and an action target needs its action type. A null target and a pipeline target without an id
    /// (meaning "no binding") are valid.
    /// </summary>
    public static async Task<Result> ValidateAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        BindingTarget? target,
        CancellationToken ct = default
    )
    {
        if (target is null)
            return Result.Success();

        switch (target.Kind)
        {
            case BindingTargetKind.Command:
                if (
                    !Guid.TryParse(target.Id, out Guid commandId)
                    || !await db.Commands.AnyAsync(
                        c => c.Id == commandId && c.BroadcasterId == broadcasterId,
                        ct
                    )
                )
                    return Result.Failure(
                        "That command does not exist in this channel.",
                        "VALIDATION_FAILED"
                    );
                return Result.Success();
            case BindingTargetKind.Action:
                return string.IsNullOrWhiteSpace(target.ActionType)
                    ? Result.Failure("An action target needs an action type.", "VALIDATION_FAILED")
                    : Result.Success();
            default:
                return Guid.TryParse(target.Id, out Guid pipelineId)
                        ? await db.EnsurePipelineInChannelAsync(broadcasterId, pipelineId, ct)
                    : string.IsNullOrEmpty(target.Id) ? Result.Success()
                    : Result.Failure(
                        "That pipeline does not exist in this channel.",
                        "VALIDATION_FAILED"
                    );
        }
    }

    /// <summary>
    /// Writes a validated request target to the row. A null target leaves the row unchanged. A pipeline
    /// target without an id means "no binding" and clears it. A pipeline target equal to the row's existing
    /// pipeline leaves no explicit command or action behind. Any other target is written as sent.
    /// </summary>
    public static void ApplyTo(
        IBindingTargetHolder holder,
        BindingTarget? target,
        Guid? existingPipelineId
    )
    {
        if (target is null)
            return;

        if (target.Kind != BindingTargetKind.Pipeline)
        {
            holder.SetBindingTarget(target);
            return;
        }

        if (string.IsNullOrEmpty(target.Id) || Guid.Parse(target.Id) == Guid.Empty)
        {
            holder.SetBindingTarget(null);
            return;
        }

        Guid pipelineId = Guid.Parse(target.Id);
        if (existingPipelineId == pipelineId)
        {
            holder.SetBindingTarget(null);
            holder.LegacyPipelineId = pipelineId;
            return;
        }

        holder.SetBindingTarget(target);
    }
}
