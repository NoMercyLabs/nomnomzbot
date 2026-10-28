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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Who runs a binding target and with what input, built by the surface that fires it. A null
/// <paramref name="UserId"/> is a system invoker: the channel's bot identity with the broadcaster role.
/// </summary>
/// <param name="UserId">The platform user id of the human the trigger names, or null for no human.</param>
/// <param name="DisplayName">The human's display name; ignored for a system invoker.</param>
/// <param name="Args">The surface's text (reward input, trigger message, API args); empty when none.</param>
/// <param name="TriggerSource">Value of the <c>trigger.source</c> variable: reward, timer, event, automation, ...</param>
/// <param name="EffectiveRole">The human's role token as chat resolves it; null means viewer. Ignored for a system invoker.</param>
/// <param name="Variables">Extra variables the surface supplies (an automation key's own settings); they are merged
/// over the built-in ones except the identity and trigger variables, which the invoker always sets itself.</param>
public sealed record InvocationContext(
    string? UserId,
    string? DisplayName,
    string Args,
    string TriggerSource,
    string? EffectiveRole = null,
    IReadOnlyDictionary<string, string>? Variables = null
);
