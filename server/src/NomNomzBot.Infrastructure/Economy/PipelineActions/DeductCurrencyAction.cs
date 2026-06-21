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
using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Infrastructure.Economy.PipelineActions;

/// <summary>
/// Pipeline action <c>deduct_currency</c> (economy.md §6): debits the triggering viewer through the ledger
/// (EntryType <c>spend_pipeline</c>). Params: <c>amount</c> (positive int), optional <c>reason</c>. Returns
/// <see cref="ActionResult.Failure"/> (stopping the pipeline) on insufficient funds. Writes the new balance into
/// <c>{{balance}}</c>.
/// </summary>
public sealed class DeductCurrencyAction(ICurrencyAccountService accounts) : ICommandAction
{
    public string ActionType => "deduct_currency";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewer))
            return ActionResult.Failure("deduct_currency requires a valid triggering viewer.");

        int amount = action.GetInt("amount");
        if (amount <= 0)
            return ActionResult.Failure("deduct_currency requires a positive 'amount'.");

        Result<CurrencyLedgerEntryDto> result = await accounts.PostLedgerEntryAsync(
            ctx.BroadcasterId,
            new PostLedgerEntryCommand(
                viewer,
                -amount,
                nameof(CurrencyEntryType.SpendPipeline),
                nameof(CurrencyLedgerSourceType.Pipeline),
                SourceId: null,
                EventId: null,
                action.GetString("reason"),
                ActorUserId: null,
                IdempotencyKey: null
            ),
            ctx.CancellationToken
        );
        if (result.IsFailure)
            return ActionResult.Failure(result.ErrorMessage ?? "deduct_currency failed.");

        ctx.Variables["balance"] = result.Value.BalanceAfter.ToString();
        return ActionResult.Success(result.Value.BalanceAfter.ToString());
    }
}
