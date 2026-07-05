// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// Routes an already signature-verified Discord interaction payload and produces the interaction response
/// (Discord's own wire JSON, returned verbatim as the HTTP 200 body within Discord's 3-second deadline):
/// <list type="bullet">
///   <item><c>PING</c> (type 1) → <c>PONG</c> (<c>{"type":1}</c>) — the endpoint-registration handshake;</item>
///   <item><c>MESSAGE_COMPONENT</c> (type 3) with <c>custom_id = notify_optin:{roleId:N}</c> — the opt-in
///     button posted by <c>PostOptInButtonAsync</c> — toggles the member's notify-role opt-in through
///     <see cref="IDiscordNotificationRoleService"/> (source <c>"button"</c>) and answers with an ephemeral
///     <c>CHANNEL_MESSAGE_WITH_SOURCE</c> (type 4, flags 64) confirmation;</item>
///   <item>anything else → an ephemeral "not supported" type-4 reply (the shared button message is never
///     rewritten for a per-member action, so UPDATE_MESSAGE (7) is deliberately not used).</item>
/// </list>
/// A body that is not a parseable interaction payload fails with <c>VALIDATION_FAILED</c>.
/// </summary>
public interface IDiscordInteractionService
{
    /// <summary>Handles one interaction delivery; returns the interaction-response JSON to answer with.</summary>
    Task<Result<string>> HandleAsync(string rawBody, CancellationToken ct = default);
}
