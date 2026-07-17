// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Obs.Entities;

/// <summary>
/// A channel's OBS control configuration (obs-control.md §1, schema P.14) — one row per channel.
/// <see cref="Mode"/> selects the transport plane: <c>direct</c> (self-host — the server connects to
/// OBS-WebSocket v5 at <see cref="Host"/>:<see cref="Port"/>) or <c>bridge</c> (SaaS/remote — a
/// browser-source bridge inside OBS connects out with <see cref="BridgeToken"/>). The OBS-WS password
/// is stored ONLY as a sealed envelope (<see cref="PasswordCipher"/>, PII-shred custody) and is never
/// readable back through any API. Runtime state (leader, cached scene) lives in the cache, not here.
/// </summary>
public class ObsConnection : SoftDeletableEntity, ITenantScoped
{
    /// <summary>Default event-subscription mask: the low-volume default bits 0–11 (obs-control.md §6).</summary>
    public const int DefaultEventSubscriptionsMask = 0xFFF;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One OBS connection per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [MaxLength(10)]
    public string Mode { get; set; } = "direct";

    /// <summary>OBS-WebSocket host for direct mode.</summary>
    [MaxLength(255)]
    public string? Host { get; set; } = "127.0.0.1";

    /// <summary>OBS-WebSocket port for direct mode.</summary>
    public int? Port { get; set; } = 4455;

    /// <summary>The OBS-WS password as a sealed envelope — never plaintext, never echoed.</summary>
    public string? PasswordCipher { get; set; }

    /// <summary>
    /// The bridge's connect credential (distinct from — and higher-privilege than — the overlay
    /// token); rotatable, unique platform-wide.
    /// </summary>
    [MaxLength(36)]
    public string? BridgeToken { get; set; }

    /// <summary>Bitmask of subscribed OBS event groups (obs-control.md §6).</summary>
    public int EventSubscriptionsMask { get; set; } = DefaultEventSubscriptionsMask;

    public bool IsEnabled { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    [MaxLength(300)]
    public string? LastError { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
