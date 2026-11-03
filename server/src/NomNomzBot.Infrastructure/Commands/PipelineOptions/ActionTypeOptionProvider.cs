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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies every pipeline action the platform registered for the <c>action_type</c> picker kind. The saved value is
/// the action type string. The registered actions are the platform's own, so the list holds no channel data.
/// </summary>
internal sealed class ActionTypeOptionProvider : IPipelineOptionProvider
{
    private readonly IEnumerable<ICommandAction> _actions;

    public ActionTypeOptionProvider(IEnumerable<ICommandAction> actions)
    {
        _actions = actions;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ActionType;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            PipelineOptionPage.Of(
                _actions
                    .Select(a => a.ActionType)
                    .Distinct()
                    .Select(t => PipelineOptionPage.Option(t, t)),
                request
            )
        );
}
