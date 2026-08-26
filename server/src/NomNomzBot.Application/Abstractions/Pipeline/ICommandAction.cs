// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Abstractions.Pipeline;

public interface ICommandAction
{
    string ActionType { get; }

    /// <summary>
    /// Palette grouping for the pipeline builder (e.g. chat, moderation, music, obs, economy) — a
    /// <see cref="LocalizedText"/> KEY, never a bare literal (S-SCHEMA-I18N-d). Categories are a small closed set:
    /// every action in the same domain folder shares ONE category key (e.g. every chat action uses
    /// <c>pipeline.category.chat</c>) rather than minting a key per action. Part of the self-describing catalogue
    /// (commands-pipelines.md §3.13) the builder renders from — see the <c>GET pipelines/actions</c> endpoint.
    /// </summary>
    LocalizedText Category { get; }

    /// <summary>
    /// Short human-readable description shown in the builder — a <see cref="LocalizedText"/> KEY, never a bare
    /// literal (S-SCHEMA-I18N-d). Unlike <see cref="Category"/>, this is unique per action.
    /// </summary>
    LocalizedText Description { get; }

    /// <summary>
    /// The structured schema of this action's <see cref="ActionDefinition.Parameters"/> — one entry per
    /// configuration field, each carrying a <see cref="PipelineActionFieldKind"/> the step form renders as a
    /// typed control (number/boolean/enum/resource picker) instead of free text (S045). Defaults to empty for
    /// actions that take no configuration.
    /// </summary>
    IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];

    /// <summary>
    /// True when this action already resolves its own <see cref="PipelineActionFieldDescriptor.Templated"/>
    /// fields internally (via its own injected template resolver) before this method returns — e.g.
    /// <c>play_tts</c> resolving its text/voice, the chat send actions resolving their message. When true,
    /// the pipeline engine's leaf executor skips its own template-resolution pass for this action entirely,
    /// so a field is never resolved twice (S-PIPE-TREE-d2b(b)). Defaults to <c>false</c> — the common case,
    /// where the engine's central pass does the resolving and the action reads already-rendered values.
    /// </summary>
    bool ResolvesOwnTemplates => false;

    Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action);
}
