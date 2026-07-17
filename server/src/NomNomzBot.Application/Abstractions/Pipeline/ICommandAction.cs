// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

public interface ICommandAction
{
    string ActionType { get; }

    /// <summary>
    /// Palette grouping for the pipeline builder (e.g. chat, moderation, music, obs, economy). Default
    /// implementation returns <c>"general"</c>; an action may override to sort itself into a group. Part of the
    /// self-describing catalogue (commands-pipelines.md §3.13) the builder renders from — see the
    /// <c>GET pipelines/actions</c> endpoint.
    /// </summary>
    string Category => "general";

    /// <summary>Short human-readable description shown in the builder; defaults to the action type.</summary>
    string Description => ActionType;

    Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action);
}
