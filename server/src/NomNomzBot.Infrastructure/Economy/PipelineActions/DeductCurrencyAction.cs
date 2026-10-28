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

using NomNomzBot.Application.Abstractions.Localization;
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

    public LocalizedText Category => new("pipeline.category.economy");

    public LocalizedText Description => new("pipeline.deduct_currency.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "amount",
                PipelineActionFieldKind.Number,
                Required: true,
                Description: new("pipeline.deduct_currency.amount.help")
            ),
            new(
                "reason",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.deduct_currency.reason.help")
            ),
        ];

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
            new(
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
