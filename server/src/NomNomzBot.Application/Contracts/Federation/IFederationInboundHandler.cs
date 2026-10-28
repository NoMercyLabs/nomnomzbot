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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Federation;

/// <summary>
/// One inbound-event applier, owned by the subsystem that owns the apply (federation-oidc.md §3.7): moderation
/// ships the ban handler, economy the trust/savings handlers. The set of registered handlers' <see cref="Type"/>
/// values IS the closed inbound accept-set — an envelope whose <c>FederatedEventType</c> matches no handler is
/// rejected <c>schema_invalid</c> (fail-closed, never silently dropped). This inverts the dependency cleanly:
/// federation defines the abstraction; subsystems depend on it, never the reverse — federation references no
/// subsystem payload type.
/// </summary>
public interface IFederationInboundHandler
{
    /// <summary>The single <c>FederatedEventType</c> this handler accepts, e.g. <c>moderation.ban.shared</c>.</summary>
    string Type { get; }

    /// <summary>
    /// The <c>ChannelFederationOptIns.OptInType</c> that gates this event type. The translator confirms an enabled
    /// <c>accept|both</c> opt-in for <c>(peerId, this)</c> on each resolved target BEFORE invoking — the handler
    /// never re-checks the gate.
    /// </summary>
    string GatingOptInType { get; }

    /// <summary>
    /// Deserializes <see cref="FederationEventEnvelope.PayloadJson"/> into this subsystem's typed event and applies
    /// it through this subsystem's own service, for exactly ONE target channel. Idempotent on
    /// <c>(envelope.EventId, targetBroadcasterId)</c>. Fails closed (<c>schema_invalid</c>) on a payload-schema
    /// mismatch.
    /// </summary>
    Task<Result> ApplyAsync(
        Guid peerId,
        Guid targetBroadcasterId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    );
}
