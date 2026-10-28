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
