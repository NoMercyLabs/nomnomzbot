// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
