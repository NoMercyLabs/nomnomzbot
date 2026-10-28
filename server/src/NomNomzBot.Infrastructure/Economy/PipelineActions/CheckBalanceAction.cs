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
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Infrastructure.Economy.PipelineActions;

/// <summary>
/// Pipeline action <c>check_balance</c> (economy.md §6): reads the triggering viewer's balance and writes it
/// into <c>ctx.Variables[set_var ?? "balance"]</c> for downstream steps. Params: optional <c>min</c> (when set,
/// returns <see cref="ActionResult.Failure"/> — gating the pipeline — if the balance is below it) and optional
/// <c>set_var</c> (the variable name).
/// </summary>
public sealed class CheckBalanceAction(ICurrencyAccountService accounts) : ICommandAction
{
    public string ActionType => "check_balance";

    public LocalizedText Category => new("pipeline.category.economy");

    public LocalizedText Description => new("pipeline.check_balance.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "set_var",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.check_balance.set_var.help"),
                DeclaresVariable: true,
                DeclaredVariableDefault: "balance"
            ),
            new(
                "min",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.check_balance.min.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(ctx.TriggeredByUserId, out Guid viewer))
            return ActionResult.Failure("check_balance requires a valid triggering viewer.");

        Result<long> balance = await accounts.GetBalanceAsync(
            ctx.BroadcasterId,
            viewer,
            ctx.CancellationToken
        );
        if (balance.IsFailure)
            return ActionResult.Failure(balance.ErrorMessage ?? "check_balance failed.");

        string variable = action.GetString("set_var") ?? "balance";
        ctx.Variables[variable] = balance.Value.ToString();

        int min = action.GetInt("min", int.MinValue);
        if (min != int.MinValue && balance.Value < min)
            return ActionResult.Failure($"Balance {balance.Value} is below the required {min}.");

        return ActionResult.Success(balance.Value.ToString());
    }
}
