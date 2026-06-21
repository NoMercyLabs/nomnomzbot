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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Infrastructure.Economy.PipelineActions;

/// <summary>
/// Pipeline action <c>jar_contribute</c> (economy.md §6): contributes the triggering viewer's currency to a
/// savings jar via <see cref="ISavingsJarService"/> (membership + caps + jar-open enforced in the service).
/// Params: <c>jar_id</c> (guid), <c>amount</c> (positive int). Returns the jar balance after.
/// </summary>
public sealed class JarContributeAction(ISavingsJarService jars) : ICommandAction
{
    public string ActionType => "jar_contribute";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewer))
            return ActionResult.Failure("jar_contribute requires a valid triggering viewer.");
        if (!Guid.TryParse(action.GetString("jar_id"), out Guid jarId))
            return ActionResult.Failure("jar_contribute requires a valid 'jar_id'.");

        int amount = action.GetInt("amount");
        if (amount <= 0)
            return ActionResult.Failure("jar_contribute requires a positive 'amount'.");

        Result<JarMovementDto> result = await jars.ContributeAsync(
            ctx.BroadcasterId,
            new JarContributeRequest(jarId, viewer, amount),
            ctx.CancellationToken
        );
        if (result.IsFailure)
            return ActionResult.Failure(result.ErrorMessage ?? "jar_contribute failed.");

        return ActionResult.Success(result.Value.JarBalanceAfter.ToString());
    }
}
