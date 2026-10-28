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

using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>
/// An entity row that holds a <see cref="BindingTarget"/> (reward, timer, event response, chat trigger,
/// scheduled task, giveaway prize, catalog item). The legacy pipeline column stays as it was; these four
/// columns sit beside it. Read the effective target with <see cref="BindingTargetExtensions.GetBindingTarget"/>
/// and write it with <see cref="BindingTargetExtensions.SetBindingTarget"/>; never read the columns by hand.
/// </summary>
public interface IBindingTargetHolder
{
    /// <summary>Which kind the row runs. Existing rows read as <see cref="BindingTargetKind.Pipeline"/>.</summary>
    BindingTargetKind TargetKind { get; set; }

    /// <summary>The command to run when <see cref="TargetKind"/> is <see cref="BindingTargetKind.Command"/>.</summary>
    Guid? TargetCommandId { get; set; }

    /// <summary>The registered action type when <see cref="TargetKind"/> is <see cref="BindingTargetKind.Action"/>.</summary>
    string? TargetActionType { get; set; }

    /// <summary>JSON object of the action's parameters (kind action only).</summary>
    string? TargetActionParamsJson { get; set; }

    /// <summary>The row's legacy pipeline column (<c>PipelineId</c> / <c>PrizePipelineId</c>), used when the kind is pipeline.</summary>
    Guid? LegacyPipelineId { get; set; }
}
