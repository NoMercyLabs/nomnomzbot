// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
public sealed record InvocationContext(
    string? UserId,
    string? DisplayName,
    string Args,
    string TriggerSource,
    string? EffectiveRole = null
);
