// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Federation.Events;

/// <summary>Raised after a peer transitions to trusted (handshake accepted). Drives bus subscription + JWKS prefetch.</summary>
public sealed class FederationPeerTrustedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required string InstanceId { get; init; }
    public required string DeploymentMode { get; init; }
    public string? BaseUrl { get; init; }
}

/// <summary>Raised after a peer is revoked or blocked. Drives bus unsubscription + key deactivation.</summary>
public sealed class FederationPeerRevokedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required string InstanceId { get; init; }
    public required string Reason { get; init; } // "manual" | "key_compromise" | "blocklist"
    public required bool Blocked { get; init; } // true => blocked, false => revoked
}

/// <summary>Raised when a channel enables/disables a federation opt-in. Drives share eligibility + accept filter.</summary>
public sealed class ChannelFederationOptInChangedEvent : DomainEventBase
{
    public required Guid OptInBroadcasterId { get; init; }
    public Guid? PeerId { get; init; } // null = any trusted peer
    public required string OptInType { get; init; }
    public required string Direction { get; init; }
    public required bool IsEnabled { get; init; }
}

/// <summary>
/// Raised after an inbound peer event passes signature + trust + opt-in + idempotency and is journaled. This is a
/// *claim*, not an authorization verdict — local handlers decide whether to act.
/// </summary>
public sealed class FederatedEventReceivedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required Guid JournalEventId { get; init; }
    public required string FederatedEventType { get; init; }
    public Guid? TargetBroadcasterId { get; init; } // null = directory-level
    public required long StreamPosition { get; init; }
}

/// <summary>Raised after an outbound event is signed and accepted by the transport for delivery to a peer.</summary>
public sealed class FederatedEventDispatchedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required Guid JournalEventId { get; init; }
    public required string FederatedEventType { get; init; }
    public required string KeyId { get; init; }
}

/// <summary>Raised when an inbound peer event is rejected (bad signature, untrusted peer, no opt-in, replay). Audit.</summary>
public sealed class FederatedEventRejectedEvent : DomainEventBase
{
    public Guid? PeerId { get; init; } // null if peer unknown
    public required string Reason { get; init; } // signature_invalid | algorithm_unsupported | peer_untrusted | no_opt_in | replay | schema_invalid | key_unknown
    public required string FederatedEventType { get; init; }
    public string? PeerInstanceId { get; init; }
}
