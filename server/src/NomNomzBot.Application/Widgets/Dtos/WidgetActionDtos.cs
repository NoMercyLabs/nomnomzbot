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

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// One pipeline action a widget asks the bot to run (widget-sdk.md §8): the same action types and parameters a
/// pipeline step uses. <paramref name="Variables"/> seeds the action's context, so a widget can hand over values
/// such as <c>redemption.id</c> exactly as a redemption-triggered pipeline would.
/// </summary>
public sealed record WidgetActionRequest(
    Guid BroadcasterId,
    Guid WidgetId,
    string ActionType,
    IReadOnlyDictionary<string, JsonElement>? Parameters,
    IReadOnlyDictionary<string, string>? Variables
);

/// <summary>
/// What the action did. <paramref name="Variables"/> is the context after the run, so outputs such as
/// <c>tts.audioUrl</c> and <c>tts.durationMs</c> reach the widget.
/// </summary>
public sealed record WidgetActionOutcome(
    bool Succeeded,
    string? Output,
    string? Error,
    IReadOnlyDictionary<string, string> Variables
);
