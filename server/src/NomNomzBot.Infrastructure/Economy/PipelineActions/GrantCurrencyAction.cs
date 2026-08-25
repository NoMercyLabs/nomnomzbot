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
/// Pipeline action <c>grant_currency</c> (economy.md §6): credits the triggering viewer through the ledger
/// (EntryType <c>earn_pipeline</c>). Params: <c>amount</c> (positive int), optional <c>reason</c>. Writes the
/// new balance into <c>{{balance}}</c> and returns it. Fails closed (currency disabled, etc.).
/// </summary>
public sealed class GrantCurrencyAction(ICurrencyAccountService accounts) : ICommandAction
{
    public string ActionType => "grant_currency";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "amount",
                PipelineActionFieldKind.Number,
                Required: true,
                Description: new(
                    "pipeline.grant_currency.amount.help",
                    "How much of the channel's currency to add to the viewer's balance.",
                    "Hoeveel van de valuta van het kanaal wordt toegevoegd aan het saldo van de kijker."
                )
            ),
            new(
                "reason",
                PipelineActionFieldKind.Text,
                Description: new(
                    "pipeline.grant_currency.reason.help",
                    "Shown on the viewer's transaction history.",
                    "Wordt getoond in de transactiegeschiedenis van de kijker."
                )
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewer))
            return ActionResult.Failure("grant_currency requires a valid triggering viewer.");

        int amount = action.GetInt("amount");
        if (amount <= 0)
            return ActionResult.Failure("grant_currency requires a positive 'amount'.");

        Result<CurrencyLedgerEntryDto> result = await accounts.PostLedgerEntryAsync(
            ctx.BroadcasterId,
            new(
                viewer,
                amount,
                nameof(CurrencyEntryType.EarnPipeline),
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
            return ActionResult.Failure(result.ErrorMessage ?? "grant_currency failed.");

        ctx.Variables["balance"] = result.Value.BalanceAfter.ToString();
        return ActionResult.Success(result.Value.BalanceAfter.ToString());
    }
}
