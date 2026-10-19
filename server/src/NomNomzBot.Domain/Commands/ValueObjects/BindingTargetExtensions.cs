// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>The one reader and writer of an entity's binding target, so no surface re-implements the column mapping.</summary>
public static class BindingTargetExtensions
{
    /// <summary>
    /// The row's effective target. Kind pipeline reads the legacy pipeline column, so a row saved before the
    /// target columns existed still resolves to its pipeline. Returns null when nothing is bound.
    /// </summary>
    public static BindingTarget? GetBindingTarget(this IBindingTargetHolder holder) =>
        holder.TargetKind switch
        {
            BindingTargetKind.Command when holder.TargetCommandId is { } commandId => new(
                BindingTargetKind.Command,
                commandId.ToString()
            ),
            BindingTargetKind.Action when !string.IsNullOrWhiteSpace(holder.TargetActionType) =>
                new(
                    BindingTargetKind.Action,
                    ActionType: holder.TargetActionType,
                    Params: string.IsNullOrWhiteSpace(holder.TargetActionParamsJson)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                            holder.TargetActionParamsJson
                        )
                ),
            BindingTargetKind.Pipeline when holder.LegacyPipelineId is { } pipelineId => new(
                BindingTargetKind.Pipeline,
                pipelineId.ToString()
            ),
            _ => null,
        };

    /// <summary>
    /// Writes <paramref name="target"/> to the row. A pipeline target also fills the legacy pipeline column
    /// (old clients keep reading it); any other kind clears it so the row never holds two targets.
    /// </summary>
    public static void SetBindingTarget(this IBindingTargetHolder holder, BindingTarget? target)
    {
        holder.TargetKind = target?.Kind ?? BindingTargetKind.Pipeline;
        holder.TargetCommandId = null;
        holder.TargetActionType = null;
        holder.TargetActionParamsJson = null;
        holder.LegacyPipelineId = null;

        if (target is null)
            return;

        switch (target.Kind)
        {
            case BindingTargetKind.Command:
                holder.TargetCommandId = Guid.Parse(target.Id!);
                break;
            case BindingTargetKind.Pipeline:
                holder.LegacyPipelineId = Guid.Parse(target.Id!);
                break;
            case BindingTargetKind.Action:
                holder.TargetActionType = target.ActionType;
                holder.TargetActionParamsJson = target.Params is null
                    ? null
                    : JsonSerializer.Serialize(target.Params);
                break;
        }
    }
}
