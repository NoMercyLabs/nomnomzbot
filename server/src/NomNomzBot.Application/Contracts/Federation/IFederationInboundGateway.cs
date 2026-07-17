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
/// The transport-independent inbound leg of the remote event bus (federation-oidc.md §3.5,
/// <c>IRemoteEventBus.ReceiveInboundAsync</c>). Runs the fail-closed gate sequence on a signed peer envelope —
/// signature verify → peer <c>TrustState=trusted</c> + <c>OriginInstanceId</c> binding → replay guard → opt-in
/// gate (directed) → append to <c>EventJournal</c> (<c>Source=federation</c>) → translate + apply — emitting
/// <c>FederatedEventReceivedEvent</c> on accept and <c>FederatedEventRejectedEvent</c> (with reason) on any gate
/// failure. Idempotent on <c>EventId</c>.
/// <para>
/// This is split out from the full <c>IRemoteEventBus</c> because inbound is always controller-driven (the mTLS
/// <c>/federation/inbound</c> endpoint), whereas the outbound / subscribe legs are the deployment-variant
/// transport (Redis vs WebSocket) that is registered separately.
/// </para>
/// </summary>
public interface IFederationInboundGateway
{
    /// <summary>
    /// Verifies, gates, journals, and applies one inbound peer envelope. <paramref name="peerId"/> is resolved
    /// upstream from the validated mTLS client-cert thumbprint. Fail-closed at every gate; the returned failure
    /// carries the same reason code published on the audit <c>FederatedEventRejectedEvent</c>.
    /// </summary>
    Task<Result<FederationInboundOutcome>> ReceiveInboundAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The result of an accepted inbound envelope: its journal id, allocated stream position, and whether at
/// least one local channel actually applied the claim (false = accepted-but-noop, no channel opted in).</summary>
public sealed record FederationInboundOutcome(Guid EventId, long StreamPosition, bool Applied);
