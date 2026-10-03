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
    /// <summary>The id of the trusted peer instance in this system.</summary>
    public required Guid PeerId { get; init; }

    /// <summary>The id that the peer instance uses for itself.</summary>
    public required string InstanceId { get; init; }

    /// <summary>How the peer is deployed, as text.</summary>
    public required string DeploymentMode { get; init; }

    /// <summary>The web address of the peer. Empty when the peer has none.</summary>
    public string? BaseUrl { get; init; }
}

/// <summary>Raised after a peer is revoked or blocked. Drives bus unsubscription + key deactivation.</summary>
public sealed class FederationPeerRevokedEvent : DomainEventBase
{
    /// <summary>The id of the peer instance in this system.</summary>
    public required Guid PeerId { get; init; }

    /// <summary>The id that the peer instance uses for itself.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Why trust ended, as the person who revoked the peer wrote it.</summary>
    public required string Reason { get; init; } // "manual" | "key_compromise" | "blocklist"

    /// <summary>True when the peer is also blocked. False when trust was only revoked.</summary>
    public required bool Blocked { get; init; } // true => blocked, false => revoked
}

/// <summary>Raised when a channel enables/disables a federation opt-in. Drives share eligibility + accept filter.</summary>
public sealed class ChannelFederationOptInChangedEvent : DomainEventBase
{
    /// <summary>The id of the channel that changed its choice.</summary>
    public required Guid OptInBroadcasterId { get; init; }

    /// <summary>The id of the peer the choice is for. Empty when it applies to any trusted peer.</summary>
    public Guid? PeerId { get; init; } // null = any trusted peer

    /// <summary>The kind of data the choice is about, as text.</summary>
    public required string OptInType { get; init; }

    /// <summary>Whether the choice is for sending or for receiving, as text.</summary>
    public required string Direction { get; init; }

    /// <summary>True when the choice is now on.</summary>
    public required bool IsEnabled { get; init; }
}

/// <summary>
/// Raised after an inbound peer event passes signature + trust + opt-in + idempotency and is journaled. This is a
/// *claim*, not an authorization verdict — local handlers decide whether to act.
/// </summary>
public sealed class FederatedEventReceivedEvent : DomainEventBase
{
    /// <summary>The id of the peer that sent the event.</summary>
    public required Guid PeerId { get; init; }

    /// <summary>The id of the stored copy of the event in the journal.</summary>
    public required Guid JournalEventId { get; init; }

    /// <summary>The type of the received event, as text.</summary>
    public required string FederatedEventType { get; init; }

    /// <summary>The id of the channel the event is for. Empty when the event is for the directory and not one channel.</summary>
    public Guid? TargetBroadcasterId { get; init; } // null = directory-level

    /// <summary>The position of the event in the peer's event stream.</summary>
    public required long StreamPosition { get; init; }
}

/// <summary>Raised after an outbound event is signed and accepted by the transport for delivery to a peer.</summary>
public sealed class FederatedEventDispatchedEvent : DomainEventBase
{
    /// <summary>The id of the peer that gets the event.</summary>
    public required Guid PeerId { get; init; }

    /// <summary>The id of the stored copy of the event in the journal.</summary>
    public required Guid JournalEventId { get; init; }

    /// <summary>The type of the sent event, as text.</summary>
    public required string FederatedEventType { get; init; }

    /// <summary>The id of the key that signed the event.</summary>
    public required string KeyId { get; init; }
}

/// <summary>Raised when an inbound peer event is rejected (bad signature, untrusted peer, no opt-in, replay). Audit.</summary>
public sealed class FederatedEventRejectedEvent : DomainEventBase
{
    /// <summary>The id of the peer that sent the event. Empty when the peer is not known.</summary>
    public Guid? PeerId { get; init; } // null if peer unknown

    /// <summary>Why the event was refused, as a short code: signature_invalid, key_unknown, algorithm_unsupported, peer_untrusted, no_opt_in, replay or schema_invalid.</summary>
    public required string Reason { get; init; } // signature_invalid | algorithm_unsupported | peer_untrusted | no_opt_in | replay | schema_invalid | key_unknown

    /// <summary>The type of the refused event, as text.</summary>
    public required string FederatedEventType { get; init; }

    /// <summary>The id that the sending instance uses for itself. Empty when it is not known.</summary>
    public string? PeerInstanceId { get; init; }
}
