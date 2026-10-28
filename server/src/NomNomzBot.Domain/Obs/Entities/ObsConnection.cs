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
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One OBS connection per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [MaxLength(10)]
    public string Mode { get; set; } = ObsConnectionDefaults.Mode;

    /// <summary>OBS-WebSocket host for direct mode.</summary>
    [MaxLength(255)]
    public string? Host { get; set; } = ObsConnectionDefaults.Host;

    /// <summary>OBS-WebSocket port for direct mode.</summary>
    public int? Port { get; set; } = ObsConnectionDefaults.Port;

    /// <summary>The OBS-WS password as a sealed envelope — never plaintext, never echoed.</summary>
    public string? PasswordCipher { get; set; }

    /// <summary>
    /// The bridge's connect credential (distinct from — and higher-privilege than — the overlay
    /// token); rotatable, unique platform-wide.
    /// </summary>
    [MaxLength(36)]
    public string? BridgeToken { get; set; }

    /// <summary>Bitmask of subscribed OBS event groups (obs-control.md §6).</summary>
    public int EventSubscriptionsMask { get; set; } = ObsConnectionDefaults.EventSubscriptionsMask;

    public bool IsEnabled { get; set; } = ObsConnectionDefaults.IsEnabled;

    public DateTime? LastConnectedAt { get; set; }

    [MaxLength(300)]
    public string? LastError { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
