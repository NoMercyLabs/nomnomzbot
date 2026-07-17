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

namespace NomNomzBot.Application.Contracts.Federation;

/// <summary>
/// Envelope → typed dispatch (federation-oidc.md §3.6/§3.7). After the gateway (§3.5) verifies + journals, the
/// translator (1) selects the one <see cref="IFederationInboundHandler"/> whose <c>Type</c> equals
/// <c>envelope.FederatedEventType</c> (unknown ⇒ <c>schema_invalid</c>, fail-closed), (2) resolves the local
/// target channel(s) — the directed <c>TargetBroadcasterId</c>, or a fan-out over every channel with an enabled
/// <c>accept|both</c> opt-in for <c>(peer OR any-trusted, gatingOptInType)</c> — and (3) invokes the handler once
/// per resolved target, each idempotent on <c>(EventId, targetBroadcasterId)</c>. It hardcodes no switch and
/// references no subsystem payload type.
/// </summary>
public interface IFederationInboundTranslator
{
    /// <summary>
    /// Resolves targets and applies the matching handler once per target. Returns the number of targets the
    /// handler successfully applied to (zero = accepted-but-noop: no channel opted in). Failure carries
    /// <c>schema_invalid</c> for an unrecognized event type or a payload the handler could not deserialize.
    /// </summary>
    Task<Result<int>> TranslateAndApplyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    );
}
